# ADR-001: `8 GB RAM / 60+ FPS` Hedefi İçin Unity 6 Hibrit DOTS + C++20 SIMD DLL + Gömülü SQLite WAL + UV-Free UberShader Seçimi

- **Durum:** Kabul Edildi
- **Tarih:** 2026-10-29
- **Faz:** Faz 0
- **İlgili Anayasa Bölümleri:** `Bölüm 1.6`, `Bölüm 2.1`, `Bölüm 3.1–3.8`, `Bölüm 4.1`, `Bölüm 5.1`

## 1. Bağlam ve Problem
Stratocraft; `18.000m x 18.000m` tamamen yıkılabilir `32-bit` voxel dünyası, İki Aşamalı Domino yıkım fiziği, `240 Hz` yönlü kılıç çarpışması (`CCD`), 75 fiziksel batarya, 500 yaratıklı Kanlı Ay kuşatmaları ve görüş alanı dışı yaşayan ekosistem içerir. Tüm bu sistemlerin:
1. **`8 GB Sistem RAM` (`< 2.5 GB` Oyun RAM'i)** ve **dahili GPU (`Intel Iris Xe / AMD Vega 8`)** üzerinde **`≥ 60 FPS` (`≤ 15.0 ms`)** hızında ve **`< 16 MB` toplam doku VRAM'i** ile çalışması,
2. İki farklı bilgisayarda (**Sabah İş PC ↔ Akşam Ev PC**) yönetici onayı (`UAC`) engellerine veya NuGet/IL2CPP ikili uyumsuzluklarına takılmadan `%100` deterministik derlenmesi zorunludur.

## 2. Değerlendirilen Seçenekler
1. **Klasik MonoBehaviour + Standart PBR (`2K/4K` UV Texture) + Harici NuGet SQLitePCLRaw Mimarisi:** `GC` duraksamaları, yüksek Draw Call, yüzlerce MB VRAM tüketimi ve Unity 6 ilk açılışında eksik NuGet DLL / IL2CPP hataları üretir.
2. **Saf Özel C++ Motoru:** Düşük seviye kontrol sağlar ancak çapraz platform pencereleme, ses, UI, fizik ve sahne araçlarını sıfırdan yazmak geliştirme süresini katlar.
3. **Unity 6 (`6000.0.25f1 LTS`) Hibrit DOTS Entities + C# Burst SIMD + C++20 `StratocraftNative.dll` (Gömülü `SQLite 3.53.0 WAL` dahil) + UV-Free `StratocraftUberShader.hlsl` + Sıfır-UAC Taşınabilir Araç Zinciri:** Hem sıfır-GC ECS/Burst hızını hem de `AVX2` C++20 gücünü sıfır dış DLL kırılganlığıyla birleştirir.

## 3. Karar
**Seçenek 3** kabul edilmiştir:
- **Simülasyon:** Unity 6 DOTS Entities + C# Burst SIMD (`0 Byte/frame` GC).
- **Yerel Eklenti (`StratocraftNative.dll`):** C++20 (`AVX2/FMA` arazi/mağara/14 cevher üretimi, `LZ4 v1.10.0` + `Zstd v1.5.6` + `XXH64 v0.8.2` bölge sıkıştırma, `Monocypher v4.0.2` sabit zamanlı `Ed25519` kontrat imzalama ve kamu malı `Public Domain SQLite 3.53.0` `WAL` telemetri/dünya kayıt motoru).
  - *Gerekçe (Gömülü SQLite3 WAL):* `SQLite 3.53.0` amalgamation doğrudan `StratocraftNative.dll` içine gömülerek Unity 6 Editör, `IL2CPP` Standalone Build ve `.NET` `dotnet test` koşucusunun tek bir sıfır-bağımlılıklı P/Invoke köprüsüyle gerçek `SQLite WAL` (`telemetry_logs.db`) üretmesi ve `sqlite3.exe` CLI ile doğrudan okunabilmesi sağlanmıştır.
  - *Gerekçe (Çift Derleyici & Sıfır-UAC Taşınabilir Araç Zinciri):* `scripts/build-native.ps1` hem sistemde kurulu `MSVC v143` derleyicisini hem de `Tools/bin/llvm-mingw` altındaki taşınabilir `Clang 23 C++20 UCRT x86_64` (`-static`) derleyicisini destekler; `Tools/bin/blender-4.2` taşınabilir Blender 4.2 LTS ile birlikte İş PC ve Ev PC arasında yönetici izni (`UAC`) gerektirmeden çalışır.
- **Görsel Katman:** Model başına UV dokusu yerine `Color32` (`R=MatID, G=T^4 Sıcaklık, B=Keskinlik, A=AO`) okuyan tek paylaşımlı `StratocraftUberShader.hlsl` (`< 16 MB` toplam kaplama VRAM'i; Faz 0 ölçümü: **`3.132 MB`**).
- **3B Üretim:** İki bilgisayar arasında `%100` deterministik eşlik ve düşük VRAM uyumu için **Yol A (`Headless Blender 4.2 + Blockbench + Python trimesh/pygltflib`)** otonom varlık üretim hattı varsayılan olarak etkinleştirilmiştir.

## 4. Sonuçlar ve Doğrulama Ölçütleri
- `StratocraftNative.dll` `-Wall -Wextra -Werror` bayraklarıyla `0 Uyarı / 0 Hata` derlenir ve `6/6` `ctest` birim testiyle doğrulanır.
- `Assets/_Stratocraft/Core` C# katmanı `TreatWarningsAsErrors=true` ile `0 Uyarı / 0 Hata` derlenir ve `5/5` NUnit birim testinden (P/Invoke + 3 katmanlı SQLite WAL dahil) geçer.
- Küresel dokular (`256x16 LUT`, `16x MatCap`, `32x MicroDetail Array`, `64³ Noise3D`) toplam **`3.132 MB`** ile `< 16 MB` VRAM bütçesini kesin olarak korur.
