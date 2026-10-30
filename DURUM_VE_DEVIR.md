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
| **Devir Numarası** | `#009` (Faz 1 & Faz 2 Tüm Kod, URP 17 Forward+, Seamless 3B Ada, Domino ve Blender 4.2 Fabrikası Tamamlandı) |
| **Son Güncelleme Tarihi (ISO-8601)** | `2026-10-30T17:52:00+03:00` |
| **Ortam / Donanım** | `EV-PC` (`Windows 11 Pro`, `NVIDIA GeForce GTX 1650 4GB`, `16 GB RAM`, Unity 6 `6000.6.7f1`) |
| **Mevcut Çalışma Aşaması** | `Faz 1 & Faz 2 Tüm Kod Düzeltmeleri Derlendi ve Doğrulandı -> Sıradaki: Faz 2 Kalite Kapısı veya Sahne Renk/Görsel İnce Ayar Sonrası Faz 3` |
| **Aktif Test Durumu** | `25/25 C# NUnit PASS`, `9/9 Python PyTest PASS`, `6/6 C++ GoogleTest PASS`, `Unity 6 Console: 0 Error / 0 Warning` |
| **Kaplama VRAM Bütçesi** | `3.382 MB + 0.512 MB MicroDetailArray = ~3.89 MB < 16.0 MB` (`%100` Bütçe İçi) |

---

## 2. Tamamlanan Mimari ve Kod Paketleri (`0 Error / 0 Warning`)

1. **URP 17 Forward+, SSAO, 4-Kaskad Gölge ve SRP/DOTS Uyumlu UberShader:**
   - `Assets/_Stratocraft/Editor/StratocraftUrpBootstrap.cs` ile `Stratocraft_URP.asset` (`ForwardPlus`, `ScreenSpaceAmbientOcclusion` DepthNormals, 4-cascade soft shadows, SRP Batcher, Global Volume Neutral Tonemapping + Bloom) oluşturuldu ve `GraphicsSettings` + tüm `QualitySettings` seviyelerine bağlandı (`"No SRP present"` hatası tamamen giderildi).
   - `StratocraftInput.hlsl`, `StratocraftUberShader.hlsl` ve `StratocraftUberShader.shader` SRP Batcher & Entities Graphics (`DOTS_INSTANCING_ON`) uyumlu hale getirildi; `256x16` LUT, `16x` MatCap ve `32` katmanlı prosedürel `MicroDetailArray` bağlandı.
2. **Sıfır Çatlaklı (`0.000m` Seam) ve Altı/Falezleri Kapalı 3B Hacimsel Ada (`HybridSurfaceNetsAndShapeMesher.cs`):**
   - `18 × (maxY + 2) × 18` komşu-apronlu `GenerateSeamlessHybridChunkMesh` + `17 × (maxY + 1) × 17` dual hücre + `1/256m` IEEE-754 ikili kuantalama + Tek-Sahipli Kenar Kuralı (`+X/+Y/+Z`) ve `[BurstCompile] PaddedSurfaceNetsDualBurstJob` + sıfır-GC `BuildUnityMeshZeroGc` (`UNorm8` + `targetMesh.Clear()`) eklendi.
   - `SeamlessSurfaceNetsMesherTests.cs` ile komşu chunk sınırlarında bit-düzeyinde sıfır çatlak ve kapalı 3B alt kabuk/falez doğrulandı (`25/25 NUnit PASS`).
   - `GeodesicSupportDijkstraJob.cs` ve `TributaryLoadDagJob.cs` üç eksenli yanal kaya hapsi (`ComputeEffectiveCrushLimit`) kullanacak şekilde güncellendi.
   - `ShapeID == 1` komşu yüzey elemesi, doğal SDF araziye oturan mimari parçalarda `-0.24m` temel eteği ve `ShapeID == 63` `4×4×4` (`0.25m`) mikro-voksel yontma alt-meshleyicisi eklendi.
3. **Headless Blender 4.2 Otonom 3B Model Fabrikası ve `GlbHierarchyLoader.cs`:**
   - `Tools/AutonomousAssetFactory/generators/blender/bpy_sculpt_worker.py` ve `blender_runner.py` ile iki ayaklı/digitigrade Ash Stalker, kanatlı Anka Boss, çift boynuzlu `16x32x4` demirci istasyonu, 4 tekerlekli mancınık, yüzen klan kristali, silahlar ve 15 farklı anatomik zırh parçası dahil 32 `.glb` model gerçek Blender 4.2 (`bmesh`) ile üretildi (`9/9 pytest PASS`, Ruff `0` hata).
   - Regex tabanlı eski okuyucu yerine `JsonUtility` glTF 2.0 şeması + RH->LH üçgen sarımı + `Socket_*` Transform hiyerarşisi kuran `GlbHierarchyLoader.cs` devreye alındı.
4. **Canlı Play Mode Kontrolcüsü (`Stage1InteractivePlayController.cs`) ve Sahne Kurucu (`StratocraftStage1SceneBuilder.cs`):**
   - Kinematik Collide-and-Slide (`3-iterasyon CapsuleCast`), Bible hızları (`4.0 / 6.8 / 2.2 / 1.1 m/s`, çanta açıkken `1.2 m/s`), `Z` sürünme (`0.65m` tavan kontrolü), `3.66m` kayma, `2.20m` ledge mantle, `F5` FPS/TPS geçişi, `%15` stamina histerezisi, `EnclosedOxygenFloodFillSystem`, çoklu-chunk sınırsız Madde Kristali (`ERR_ORE_RESONANCE_IMMUNE` + canlı `StructuralCollapseSystem` Domino çöküşü), `G` fiziksel silah atma ve 3B kapaklı CAS sandığı tamamlandı.

---

## 3. Yeni Sohbette Yapay Zekanın Sıradaki İlk İşi

1. Unity açıkken `StratocraftUberShader.hlsl` son satır yenilemesini (`AssetDatabase.ImportAsset`) ve `Stratocraft/Asama 1 - Canli Dunya ve 3B Varlik Sahnesini Kur` menüsünü tetikleyerek zümrüt çimen / su / lav LUT palet renklerinin sahnede son görsel kontrolünü yap.
2. Kullanıcı onayının ardından `scripts/quality-gate.ps1 -Phase 2` çalıştırıp **Faz 3 (Temel Üretim, Hologram İnşa, 3D Çit BFS Klan ve KGP)** geliştirmesine geç.

---

## 4. Kullanıcının Özel Çalışma ve İletişim Tercihleri

1. **Doğrudan ve Şeffaf Ol, Bekletme:** Kullanıcı zamanı kısıtlı olduğunda veya soru sorduğunda arka planda gereksiz görsel ince ayar döngüsüne girip bekletme; istenen işlemi (test/commit/yanıt) hızla tamamla ve net açıkla.
2. **Game Logic Bible'a Sadık Kal:** Doğal arazide kübik Minecraft tarzına asla dönme; *Astroneer / Deep Rock Galactic / 7 Days to Die* low-poly fasetli standardını koru.
3. **Sıfır Hata ve Taşınabilirlik:** `0 Error / 0 Warning` ve sıfır sabit (`D:\oyun` vb.) mutlak yol kuralını daima koru.
