# Faz Raporu — %100 Voksel + 4×4×4 Mikro-Voksel Yerleşim Mimarisi, İç Eşyalar, Tam Yıkım Fiziği & 60+ FPS Akış Düzeltmesi (`Devir #085`)

> **Bağlayıcı Şartname:** [SORUNLARI_DUZELTME_VE_VOKSEL_MIMARI_DOKUMANI.md](../gercekci-insaat-ve-mimari-sistemi/SORUNLARI_DUZELTME_VE_VOKSEL_MIMARI_DOKUMANI.md)  
> **Devir Raporu:** [DURUM_VE_DEVIR.md](../../DURUM_VE_DEVIR.md)  
> **Tarih (ISO-8601):** `2026-11-24T15:30:00+03:00`  
> **Durum:** **TAM PASS (`quality-gate.ps1 -Phase 4`, `401/401` `.NET 10` Release Testi, `0 Error / 0 Warning`)**

---

## 1. Kalite Kapısı & Otomatik Doğrulama Özeti

| Doğrulama Katmanı | Komut / Kapsam | Sonuç | Not |
| :--- | :--- | :---: | :--- |
| **C# `.NET 10` Release Testleri** | `dotnet test Tests/StandaloneCoreTests/Stratocraft.StandaloneCoreTests.csproj -c Release` | **401 / 401 PASS** | `0 Error / 0 Warning` (`396` mevcut + `5` kapsamlı `DK-01..DK-11` voksel/mikro-eşya/gerçek fizik/performans testi) |
| **C++20 Native (`StratocraftNative`)** | `ctest --output-on-failure` | **6 / 6 PASS** | `Simd`, `WorldGen`, `LZ4`, `Zstd`, `Ed25519`, `SQLite WAL` |
| **Python 3.11 Otonom 3B Fabrika** | `ruff check` + `pytest Tools/AutonomousAssetFactory/tests` | **9 / 9 PASS** | `All checks passed!`, `T_Stratocraft_MaterialLUT_256x16.png` (`120..152` mimari derz/tuğla/taş/kiremit tonları) |
| **Kaplama VRAM Bütçesi** | `scripts/quality-gate.ps1 -Phase 4` (`Assets/Art/Generated/Textures`) | **3.133 MB / 16.000 MB** | `< 16.0 MB` bütçesinin çok altında |
| **Unity 6 Arka Plan Derleme** | `script_recompile(focus: false)` | **0 Error / 0 Warning** | Kullanıcı video çekimi kısıtına saygıyla PlayMode açılmadan ve odak çalınmadan doğrulandı |
| **4 Dondurulmuş Sıvı Dosyası** | `git diff 94f0fd6..HEAD` | **0 diff (BOŞ)** | `FluidSurfaceSkirtMesher`, `FluidCellularAutomataJob`, `StratocraftFluidSurface`, `FluidSimulationSystem` |
| **Taşınabilirlik & Güvenlik** | Mutlak yol & `.env` / `saves/` taraması | **0 İhlal** | Tamamı göreceli yol / `$PSScriptRoot`, Karar A3 (`120..152` `%100 CrystalImmune`) korundu |

---

## 2. Giderilen 5 Ana Sorun (`S1..S5`) ve Mimari Dönüşüm (`Faz D1..D5`)

1. **`S1 & S5` — Duvarları Eksik Seyrek `.glb` İskeletler Yerine `%100` Kapalı Voksel Binalar + `%100` `4×4×4` Mikro-Voksel (`ShapeID = 63`) İç Eşyalar (`DK-01, DK-02`):**
   - `VoxelSettlementArchitectureStamp.cs` (`1.832` satır) ve `VoxelMicroFurnitureTemplates.cs` (`418` satır) ile Güney Köyü (`4` kültür üslubu), Batı Kasabası (`2–3` katlı konaklar, Revaklı Han, demirci, marangoz, pazar), Kuzey `48×48` Metropolü (`3–4` katlı apartmanlar, surlar, kapılar, burçlar) ve `3×2` chunk `23` odalı 3 katlı `GrandCityPalace` doğrudan `voxels[]` (`ShapeID 1..20`) + `MicroVoxelPool4096` (`ShapeID = 63`, `0.25m` alt-voksel çözünürlük) olarak damgalandı.
   - Her bina doğal/teraslanmış zemine kadar masif taş temel (`FullMasonryCube = 1`) indirir; dış ve iç oda duvarları, kat döşemeleri, eğimli `Ramp45` çatılar + üçgen kalkan (`gable`) uç duvarları ve bacalar `%100` kapalıdır.
   - İç ve dış eşyaların tamamı (`DiningTable64`, `WorkTable64`, `ChairWithBack64`, `Stool64`, `BedHeadboard64`/`BedFoot64`, `Bookshelf64`, `StorageChest64`, `Barrel64`, `KitchenCounter64`, `CookingHearth64`, `RoyalThrone64`, `WeaponRack64`, `AnvilPedestal64`, `SawhorseBench64`, `WellWindlassFrame64`, `FencePostAndRail64`, `MullionWindow64`, `[E]` ile açılıp kapanan `HingedDoorLower/Upper64`) tek chunk mesh'inin parçasıdır (`0` ekstra entity / `0` `.glb` draw call!).
   - `bake_material_lut.py` + `StratocraftUberShader.hlsl` (`_UsePaletteLUT = 1`) ile `MaterialId 120..152` voksel yüzeylerinde prosedürel tuğla derzi, kesme taş/mermer/beton derzi, kiremit oluğu, kütük kertik derzi ve sıkıştırılmış toprak katmanı detayları aktif edildi.

2. **`S2` — Gerçek Fizik Altında Sıfır Hileyle Durma & Alt Destek Kırılınca Havada Parça Kalmaması (`DK-03, DK-04, DK-05`):**
   - Hazır dünya binalarında fizik çözücü **kesinlikle kapatılmadı / bypass edilmedi**: `StructuralMaterialProps.cs`, `StructuralCollapseSystem.cs`, `GeodesicSupportDijkstraJob.cs` (`y == 0` yüksek irtifa halo sınır koruması dahil) ve `Stage1StructuralCollapseBridge.cs` (`IsStructuralArchitecturalOrMicroVoxel`, `TryReleaseCollapsedMicroVoxelSlot`, `WorldMicroPools`) üzerinde tüm binalar gerçek çözücü altında **`0` çökme** ile ayakta durur (`DK03`).
   - Bir evin tek yan duvarından blok kırıldığında yedekli duvarlar yükü taşır (`0` çökme — `DK05`); zemin kat taşıyıcıları tamamen kesildiğinde ise üst duvarlar, döşemeler, eğimli `Ramp45` çatılar, kapılar **ve `ShapeID == 63` mikro-voksel mobilyalar** `%100` zincirleme yıkılır, havada **`0`** parça kalır ve `MicroVoxelPool4096` slotları serbest bırakılır (`DK04`).
   - Oyuncunun kendi kurduğu `.glb` yapılarda da `UnifiedStructureGraphSolver.HasPendingVisualSync` + `CityChunkLodBatchRenderer.SyncNodeStatesFromSolver` bağlanarak yıkılan çatı/kapıların havada asılı kalması önlendi.

3. **`S3 & S4` — Şehirde `3 FPS` Kasma ve Uzaklaşınca `Chunk: 36/473` Yüklenmeme Sorunlarının Çözümü (`DK-06, DK-07`):**
   - `WorldLooseBuildSystem.UseLegacyGlbSettlementInstances = false` ile yerleşim binaları için `20.000+` gölgeli `.glb` instance çizimi `0`'a indirildi; arazi + bina + iç eşya tek `HybridSurfaceNetsAndShapeMesher` chunk mesh'inde birleşti.
   - `SettlementLayoutSystem.cs` içine `[ThreadStatic]` O(1) `ChunkLookupCacheSlot` eklendi; `Stage1InteractivePlayController.cs` içindeki `HasAll8NeighborVoxels` ilk-mesh kilidi kaldırıldı ve streaming/meshing bütçeleri yükseltilerek uzaklaşırken anında chunk yüklenmesi sağlandı.

---

## 3. Kullanıcı PlayTest Kontrol Noktaları

- **Başlangıç Vadisi (`(-2.6, 96.1, -6.7)`):** Temiz doğuş alanı (`r < ~80m`).
- **Güney Köyü (`X ≈ 0..+8, Z ≈ -160`, Chunk `(0, -10)`):** Kapalı duvarlı/çatılı voksel köy evleri, `[E]` ile açılıp kapanan voksel kapılar, içeride `4×4×4` voksel masa/sandalye/yatak/şömine/sandık, kuyu çıkrığı ve tarla çitleri.
- **Batı Kasabası (`X ≈ -256, Z ≈ 0`, Chunk `(-16, 0)`):** `2–3` katlı kapalı voksel konaklar, Revaklı Han, demirci örsü/ocağı ve marangoz atölyesi.
- **Kuzey Metropolü & `23` Odalı Dev Saray (`X ≈ 0, Z ≈ +576`, Chunk `(0, +36..+38)`):** Akıcı FPS ile yüklenen `3–4` katlı voksel apartmanlar, taş surlar/kapılar ve mermer kubbeli, 3 katlı, `23` odalı voksel saray + taht/ziyafet/yatak/kütüphane mikro-voksel eşyaları.
