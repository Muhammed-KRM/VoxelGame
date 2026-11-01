# 📌 Stratocraft — Canlı Proje Durumu ve Bilgisayarlar Arası Yapay Zeka Devir Raporu (`DURUM_VE_DEVIR.md`)

> [!IMPORTANT]
> **YAPAY ZEKA AJANI İÇİN ZORUNLU BAŞLANGIÇ TALİMATI (YENİ SOHBETE VEYA YENİ OTURUMA BAŞLARKEN):**
> 1. Bu döküman projenin tek ve güncel **Devir Teslim Raporudur**.
> 2. Yeni sohbete başlar başlamaz `git status -s` ve `git log -5 --oneline --date=iso` komutlarını çalıştırıp reponun bu dökümanla birebir eşleştiğini doğrula.
> 3. Kullanıcıya uzun uzun bekleterek işlem yapma; önce aşağıdaki **"Kullanıcı Çalışma & İletişim Tercihleri"**ni oku, ardından doğrudan **Bölüm 4'teki "Sıradaki İlk İş"** adımlarını icra etmeye başla.
> 4. Hiçbir dosyada `D:\oyun` gibi mutlak (hardcoded) yol kullanma, daima göreceli yol kullan.

---

## 1. Aktif Devir Kimliği (Son Durum Özeti)

| Alan | Değer |
| :--- | :--- |
| **Devir Numarası** | `#011` (Faz 1 & Faz 2 %100 Tamamlandı, Akışkan/Kaldera ve AI/Combat Entegre Edildi, Kalite Kapısı %100 PASS) |
| **Son Güncelleme Tarihi (ISO-8601)** | `2026-11-01T19:43:00+03:00` |
| **Ortam / Donanım** | `IS-PC` (`Windows 11`, `NVIDIA GeForce RTX 3050 Ti Laptop GPU 4GB`, `16 GB RAM`, Unity 6 `6000.6.7f1`) |
| **Mevcut Çalışma Aşaması** | `Faz 1 ve Faz 2 %100 Tamamlandı ve Doğrulandı -> Sıradaki: Faz 3 (Temel Üretim, Hologram İnşa, 3D Çit BFS Klan Koruma Alanı)` |
| **Aktif Test Durumu** | `40/40 .NET 10 Core PASS`, `32/32 Unity EditMode PASS`, `9/9 Python PyTest PASS`, `6/6 C++ GoogleTest PASS`, `Unity 6 Console: 0 Error / 0 Warning` |
| **Kaplama VRAM Bütçesi** | `4.14 MB / 16.000 MB` (`%100` Bütçe İçi, Hedef: < 16.0 MB) |
| **Çalışma Belleği (RAM)** | `< 2.5 GB` |

---

## 2. Tamamlanan Sistemler ve Uygulanan Çözümler

1. **Unity C# Donanım Hızlandırmalı Bit İşlemcisi (`BitOps.cs`):**
   - Unity C# profilinde bulunmayan `System.Numerics.BitOperations` yerine `#if UNITY_5_3_OR_NEWER` altında `Unity.Mathematics.math.tzcnt / lzcnt / countbits` donanım talimatlarına, dış testlerde `System.Numerics.BitOperations`'a yönlendiren `BitOps` köprüsü yazıldı.
   - `HybridSurfaceNetsAndShapeMesher`, `NavSpanRebuildJob`, `MicroVoxelPool4096`, `FluidPorosityUtility`, `TributaryLoadDagJob`, `EnclosedOxygenFloodFillSystem`, `BinaryGreedyMeshingJob` dosyalarındaki derleme hataları tamamen giderildi.

2. **21 Kanonik Yaratık, Uzuv Koparma, AI ve Kapalı Form Çatışma (Modül F1 / M07):**
   - `Creature21BehaviorSystem.cs`: Bible §13.0 kanonik 21 yaratık (Lv1..Lv5, 140 HP Goblin'den 10.800 HP Void Stalker'a), 16 uzuv yetenek bayrağı (`AnatomyCapabilityFlags`) ve kanat/kol/bacak kopma hareket cezaları uygulandı.
   - `OffScreenEntityRecord.cs`: Q20.12 sabit noktalı koordinatlar, atomik CAS kilidi (`Interlocked.CompareExchange`), deterministik yol enterpolasyonu.
   - `HyperbolicLanchesterCombat.cs`: $O(1)$ kapalı form hiperbolik ($\cosh/\sinh$) Lanchester çözücüsü, $\rho \le 0.9995$ tekillik sınırı, 2D CPA ve süpürülmüş mayın kesişim testi.
   - `DestructibleNavSpanAStar.cs`: Ağırlıklı A* ($\epsilon = 1.25$, azami 320 düğüm), patron kutsal alan blok dokunulmazlığı koruması (`OriginState == 0`).

3. **Canlı Sahnede Gerçek Akışkan Yüzeyleri, Kaldera Havzası ve Gölgelendirici (Modül F2 / M08):**
   - `StratocraftStage1SceneBuilder.cs`: Kaldera ve su havuzu daha önce katı voksel olarak üretilip sarı/mavi taş olarak basılıyordu. Arazi mesher'ında $y \ge 7$ arası hava oyuğu açılarak gerçek çukurlar oyuldu.
   - Akışkanlar `FluidCell16.Pack(...)` ile 16-bit sonlu akışkan hücresi olarak paketlendi ve `FluidSurfaceSkirtMesher.GenerateChunkFluidSurfaceMesh` üzerinden +0.35m kıyı eteğiyle orüldü.
   - `StratocraftFluidSurface.shader`: Sıfır-doku URP 17 gölgelendirici. Su için Beer-Lambert derinlik soğurması ve Gerstner dalgaları; lav için Voronoi bazaltik çatlaklar ve Stefan-Boltzmann $T^4$ akkor emisyon uygulandı.
   - Sahnede `Stratocraft/Asama 1 - Canli Dunya ve 3B Varlik Sahnesini Kur` menüsü çalıştırıldı; lav kalderası, su havuzu, obsidyen set, 15 parça zırh askılığı, fiziksel CAS sandığı, çift boynuzlu örs, klan kristali, Ash Stalker sürüleri, Anka patronu ve mancınık sahneye yerleştirilip kaydedildi (`capture_scene_view` ve `capture_game_view` ile doğrulandı).

4. **Karakter ve Kontrolcü İnce Ayarları:**
   - `Stage1InteractivePlayController.cs`: Bible §6 kılıf çekme süreleri (Bel: 0.5s/0.6s, Sırt: 0.9s/1.0s), 'T' tuşu 90-derece mimari rotasyon desteği ve yere atılan silahların kalıcı olması (`Destroy` kaldırıldı) uygulandı.

5. **Kalite Kapısı Başarısı (`quality-gate.ps1`):**
   - VRAM Bütçesi: `4.14 MB` (16.0 MB bütçesinin çok altında).
   - `build-native.ps1`: Unity açıkken DLL kilitlenmesine karşı güvenli hale getirildi.
   - C++ GoogleTest: `6/6 PASS`
   - Python Pytest: `9/9 PASS`
   - .NET Core Test: `40/40 PASS`
   - Unity EditMode Test: `32/32 PASS`
   - Unity Console: `0 Errors, 0 Warnings`

---

## 3. Neler Kaldı? (Sıradaki Geliştirme Maddeleri)

Faz 1 ve Faz 2 mekanik ve görsel olarak eksiksiz tamamlanmıştır. Sırada **Faz 3** bulunmaktadır:

1. **Faz 3.1 — Temel Üretim & Zanaat (Crafting & Smelting):**
   - Çift boynuzlu örs üzerinde Bible §4.0 uyarınca 16x32x4 voksel çekiçleme, ısı dengesi ve külçe dökümü.
2. **Faz 3.2 — Hologram İnşaat Sistemi:**
   - 7DTD tarzı hayalet blok (ghost preview) yerleşimi, destek hattı analizi ve malzeme doldurma.
3. **Faz 3.3 — 3D Çit BFS Klan Koruma Alanı ve KGP Kristalleri:**
   - Klan kristali seviyeleri (Lv1..Lv5), 3B hacimsel BFS kapalı alan hesaplaması ve yetkisiz oyuncu CSG kırma engellemesi.

---

## 4. Yeni Sohbette / Oturumda Yapay Zekanın Sıradaki İlk İşi

Yeni oturumu açan ajan sırasıyla şu adımları izleyecektir:

1. **Adım 1 — Doğrulama:**
   - `git status -s` çalıştırıp çalışma ağacını kontrol et.
   - Unity konsolunun `0 Error / 0 Warning` olduğunu MCP `console_get` ile doğrula.
2. **Adım 2 — Sahne Kontrolü:**
   - `Assets/Scenes/StratocraftStage1Showcase.unity` sahnesinin açık olduğunu doğrula.
3. **Adım 3 — Faz 3 Mimarisini Başlat:**
   - `STRATOCRAFT_ EVRENSEL OYUN TASARIM VE MANTIK ANAYASASI (GAME LOGIC BIBLE) (2).md` dökümanının Faz 3 (Bölüm 4 - Zanaat, İnşaat ve Klan Koruma Alanı) başlığını aç.
   - Faz 3 testlerini (`Tests/StandaloneCoreTests` ve `Assets/Tests/EditMode`) yaz ve sistemleri adım adım kodla.

---

## 5. Kullanıcının Özel Çalışma ve İletişim Tercihleri

1. **Doğrudan ve Şeffaf Ol, Bekletme:** Kullanıcıya yapılan işleri net ve görsel kanıtlarla (Unity sahne görüntüleri ve test raporları) sun.
2. **Game Logic Bible'a Sadık Kal:** Doğal arazide kübik Minecraft tarzına asla dönme; *Astroneer / Deep Rock Galactic / 7 Days to Die* low-poly fasetli standardını koru.
3. **Sıfır Hata ve Taşınabilirlik:** `0 Error / 0 Warning` kuralından taviz verme ve mutlak dosya yolu (`D:\oyun` vb.) asla kullanma.
