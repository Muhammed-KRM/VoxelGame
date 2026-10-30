# ⚔️ Stratocraft (VoxelGame)

> **Sıfır Soyut Menü — `%100` Fiziksel Etkileşimli Voxel Hayatta Kalma, Mühendislik ve Klan Savaşı Oyunu**  
> **Hedef Donanım Bütçesi:** `8 GB Sistem RAM` (`< 2.5 GB` Oyun RAM) · `< 16 MB` Doku VRAM · `≥ 60 FPS` (`≤ 15.0 ms`) · Dahili GPU (`Intel Iris Xe / AMD Vega 8`) Uyumlu

---

## 1. Üçlü Proje Anayasası ve Geçiş Belgeleri

- **Geliştirici ve Faz İnşa Anayasası:** [STRATOCRAFT_GELISTIRICI_DOKUMANI.md](./STRATOCRAFT_GELISTIRICI_DOKUMANI.md)
- **Evrensel Oyun Tasarım Anayasası (`v7.0`):** [STRATOCRAFT_ EVRENSEL OYUN TASARIM VE MANTIK ANAYASASI (GAME LOGIC BIBLE) (2).md](./STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20%28GAME%20LOGIC%20BIBLE%29%20%282%29.md)
- **Matematiksel Simülasyon ve Voxel Algoritmaları:** [STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md](./STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md)
- **İki Bilgisayarlı (İş ↔ Ev) Geçiş Rehberi:** [BILGISAYARLAR_ARASI_GECIS_REHBERI.md](./BILGISAYARLAR_ARASI_GECIS_REHBERI.md)
- **Canlı Yapay Zeka Devir Raporu:** [DURUM_VE_DEVIR.md](./DURUM_VE_DEVIR.md)

---

## 2. 4 Katmanlı Mimari Özeti

1. **Simülasyon Çekirdeği:** `Unity 6.6 (6000.6.7f1)` + **DOTS Entities 6.6** + **C# Burst 2.0 SIMD** (`< 0.15 ms` Binary Greedy Meshing, İki Aşamalı Domino Yıkım DAG'ı, `240 Hz` Kılıç CCD, 3 Katmanlı `NavSpan` LoD AI).
2. **Yerel C++20 SIMD Eklentisi (`StratocraftNative`):** `AVX2` arazi gürültüsü, `LZ4/Zstd + XXH64` bölge sıkıştırma ve `Ed25519` Kan İmzalı Kontrat kriptografisi.
3. **Görsel & GPU:** URP Forward+, `GPU Resident Drawer`, tek paylaşımlı **UV-Free `StratocraftUberShader.hlsl`** (`< 16 MB VRAM`) ve `500 Mob` `FP16 VAT`.
4. **Servisler & `%100` Otonom 3B Fabrika:** 3 katmanlı fail-safe `SQLite WAL` telemetri (`telemetry_logs.db`), `HostLoopback` sunucu eşliği ve `Tools/AutonomousAssetFactory/` Headless CLI varlık üretim hattı.

---

## 3. Hızlı Başlangıç Komutları (PowerShell)

```powershell
# 1. Yeni bilgisayarda tüm derleyici ve 3B araç zincirini birebir aynı sürümlerle kur:
powershell -ExecutionPolicy Bypass -File .\scripts\setup-environment.ps1

# 2. Sabah/akşam bilgisayar geçişinde son durumu çek ve yerel DLL/dokuları üret:
powershell -ExecutionPolicy Bypass -File .\scripts\sync-pc.ps1 -Pull

# 3. C++20 StratocraftNative DLL derle + ctest çalıştır:
powershell -ExecutionPolicy Bypass -File .\scripts\build-native.ps1

# 4. Otonom 3B Fabrika küresel dokularını üret ve doğrulama testlerini çalıştır:
powershell -ExecutionPolicy Bypass -File .\scripts\generate-assets.ps1 -Phase 0

# 5. Faz kalite kapısını çalıştır:
powershell -ExecutionPolicy Bypass -File .\scripts\quality-gate.ps1 -Phase 0

# 6. Projeyi başlat / durdur:
powershell -ExecutionPolicy Bypass -File .\baslat.ps1
powershell -ExecutionPolicy Bypass -File .\durdur.ps1
```
