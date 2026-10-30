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
| **Devir Numarası** | `#008` (Faz 1 & Faz 2 Mimari Denetim ve Çözüm Raporları Tamamlandı) |
| **Son Güncelleme Tarihi (ISO-8601)** | `2026-10-30T16:55:00+03:00` |
| **Ortam / Donanım** | `EV-PC` (`Windows 11 Pro`, `NVIDIA GeForce GTX 1650 4GB`, `16 GB RAM`, Unity 6 `6000.6.7f1`) |
| **Son Git Commit (`HEAD`)** | `552ec33 docs(mimari): Faz 1 ve Faz 2 sorun tespiti, alt ajan arastirmalari ve cozum mimarisi raporlari eklendi` |
| **Mevcut Çalışma Aşaması** | `Faz 1 & Faz 2 Kök Neden Denetimi ve Çözüm Mimarisi Tamamlandı -> Sıradaki: Paket 1 (URP 17 & Uber-Shader) ve Paket 2 (Burst Padded Surface Nets)` |
| **Aktif Test Durumu** | `24/24 C# NUnit PASS`, `9/9 Python PyTest PASS`, `6/6 C++ GoogleTest PASS` (`0 Hata, 0 Uyarı`) |
| **Tamamlanan Mimari Rapor** | [`docs/FAZ1_FAZ2_SORUN_VE_COZUM_MIMARI_RAPORU.md`](docs/FAZ1_FAZ2_SORUN_VE_COZUM_MIMARI_RAPORU.md) + 6 Alt Ajan Araştırması (`docs/arastirma/`) |

---

## 2. Bu Sohbette Konuşulanlar ve Alınan Kritik Kararlar

1. **Game Logic Bible Referansı ve Görsel Tarz Düzeltmesi:**
   - Kullanıcı sahnedeki Minecraft benzeri kübik dünyayı gördüğünde projenin anayasası olan `STRATOCRAFT_ EVRENSEL OYUN TASARIM VE MANTIK ANAYASASI (GAME LOGIC BIBLE) (2).md` dökümanını işaret etti.
   - **Karar:** Oyun dünyası kesinlikle Minecraft gibi küp küp olmayacak; *Astroneer*, *Deep Rock Galactic* ve *7 Days to Die* tarzında, yumuşak eğimleri ve keskin low-poly poligon hatları olan organik bir yüzey ağı (**Surface Nets / Dual Contouring**) ile üretilecek.
2. **Küp Mesher'dan Surface Nets'e Geçiş ve Ortaya Çıkan Görsel Hatalar:**
   - Küp tabanlı mesher yerine `HybridSurfaceNetsAndShapeMesher.cs` yazıldı. Ancak ilk prototip sahnede görsel kusurlar oluşturdu (detaylar Bölüm 3'te).
3. **Kullanıcının İletişim Şartı:**
   - Kullanıcı, uzun süre sessizce arka planda çalışılıp beklenmesinden rahatsız olduğunu, ne yapılıyorsa anında ve şeffafça açıklanmasını, bir sorun varsa açıkça söylenmesini talep etti.

---

## 3. Sistemlerin Durumu: Neler Çalışıyor, Neler Çalışmıyor?

### ✅ Kusursuz ve Düzgün Çalışanlar (Tamamlananlar)
- **C++20 Çekirdek Kütüphane (`StratocraftNative.dll`):** AVX2/FMA SIMD 3B arazi gürültüsü, LZ4/Zstd sıkıştırma, Ed25519 kriptografik imzalama, SQLite WAL 3 katmanlı telemetri (`6/6 ctest PASS`).
- **Otonom 3B Varlık Fabrikası (`Tools/AutonomousAssetFactory/`):** 15 parça heykelsi zırh takımı, silahlar, örs, kristal, 21 yaratık ve patron modelleri, 4 açılı PNG doğrulama sistemi (`9/9 pytest PASS`, Ruff `0` hata, VRAM `3.382 MB < 16 MB`).
- **Fiziksel Karakter, Biyoloji ve Envanter:** 7 hareket duruşu, sürünme, 30 sn oksijen tükenmesi, 3B kumaş çanta, madde kristali vakum koruması, 64-bit CAS sandık iş parçacığı güvenliği (`24/24 NUnit PASS`).
- **Canlı Unity 6.6 Entegrasyonu:** `StratocraftStage1Showcase.unity` sahnesi ve `Stratocraft/Asama 1 - Canli Dunya ve 3B Varlik Sahnesini Kur` menü oluşturucusu.
- **Tüm Birim Testler:** `scripts/run-tests.ps1` tek komutla tüm katmanları sıfır hata ile derleyip geçmektedir.

### ❌ Düzgün Çalışmayanlar / Açık Görsel Sorunlar (ÇÖZÜLMEKTE OLANLAR)
Kullanıcının Unity sahnesinde gördüğü ve acil düzeltilmesi gereken 4 görsel problem:
1. **"Zemin kağıt gibi 3D değil, altı yok" (Hacimsiz İnce Yüzey):**
   - *Sebep:* Surface Nets algoritması katıdan havaya geçişte poligon örer. Sahne oluşturucuda `y=0` ve alt sınırlar katı bırakıldığı için algoritma sadece en üst yüzeyde geçiş gördü ve adanın altını/yanlarını örmedi; zemin 2 boyutlu havada süzülen bir kağıt gibi kaldı.
   - *Çözüm:* Arazinin altına ve dış sınırlarına hava tamponu verilerek 10-15 metre kalınlığında, altı kayalık tabanlı, yanları dik falezli gerçek bir 3B ada hacmi oluşturulacak.
2. **"Boşluklar ve haç şeklinde yarıklar var" (Chunk Dikiş Hatası):**
   - *Sebep:* 16x16'lık voxel bloğundaki 15 dual hücre 16 metrelik alana ölçeklenmediği için komşu chunk sınırlarında 1.5 - 2 metrelik dikiş boşluğu kaldı.
   - *Çözüm:* `scale14To16` katsayısı ve sürekli dünya gürültüsü ile chunk dikiş koordinatları `0.000m` farkla sıfır boşluklu (seamless) kaynaklanacak.
3. **"Low-poly smooth/faceted ışıklandırma çalışmıyor gibi" (Gölgesiz / Düz Görünüm):**
   - *Sebep:* Sahneye özel yazılan `StratocraftVertexColorUberLit.shader` materyallere bağlanmadığı için Unity yedek URP Unlit materyali kullandı; bu da yönlü ışık ve gölgelerin kaybolmasına yol açtı.
   - *Çözüm:* Faceted normal (`cross(ddx, ddy)`) ve PBR yönlü ışık/gölge hesaplayan `StratocraftVertexColorUberLit` shader'ı chunk materyallerine atanacak.
4. **"Bazı 3B modeller eski küp hallerinde duruyor":**
   - *Sebep:* Unity'nin `Assets/Art/GeneratedSceneAssets/` altındaki `.asset` önbelleği otomatik GPU tazelemesi yapmadı.
   - *Çözüm:* Sahne oluşturucuda `mesh.Clear()` ve `UploadMeshData(false)` çağrılarak yeni üretilen heykelsi low-poly modeller zorla yüklenecek.

---

## 4. Yeni Sohbette Yapay Zekanın Sıradaki İlk İşi (Hemen Yapılacaklar)

Yeni sohbetteki yapay zeka ajanı hiçbir şeyi baştan sormadan doğrudan şu sırayı takip edecektir:

1. **Adım 1 — `HybridSurfaceNetsAndShapeMesher.cs` Güncellemesi:**
   - Dual hücre koordinatlarını `(x + localPos.X - 0.5f) * (16.0f / 14.0f)` formülüyle 16 metreye tam oturt.
   - Sınır dikişlerini komşu chunk'larla sürekli kıl (boşlukları kapat).
   - Alt taban (`y=0`) ve dış falezler için üçgen üretim sınırlarını aç.
2. **Adım 2 — `StratocraftStage1SceneBuilder.cs` Güncellemesi:**
   - Araziyi `y=0` tabanı ve dış sınırları hava olacak şekilde doldur; ortada 10-15m kalınlığında altı kapalı, yanları falezli 3B bir ada kütlesi oluştur.
   - `Stratocraft/VertexColorUberLit` shader'ını arazi materyaline bağla.
   - Tüm varlık `.asset` dosyalarını `Clear()` ve `UploadMeshData(false)` ile temizleyip yeni heykelsi modelleri uygula.
3. **Adım 3 — Derleme ve Sahne Yenileme:**
   - Menü komutunu çalıştırarak canlı sahneyi güncelle (`StratocraftStage1Showcase.unity`).
   - Sahne görüntüsünü alıp kullanıcıya dökümana tam uyumlu, kalınlığı olan 3B low-poly dünyayı göster.
4. **Adım 4 — Kullanıcı Onayı:**
   - Görseli kullanıcıya sunduktan sonra kullanıcıdan onay al ve **Aşama 2 (Faz 3 — Temel Üretim, Hologram İnşa, 3D Çit BFS Klan, KGP)** geliştirmesine geç.

---

## 5. Kullanıcının Özel Çalışma ve İletişim Tercihleri (YENİ SOHBETTE KESİNLİKLE UYULMALIDIR)

1. **Doğrudan ve Şeffaf Ol:** Bir sorun olduğunda veya işler beklendiği gibi gitmediğinde gizleme, mazeret üretme; doğrudan teknik sebebini ve nasıl çözüleceğini söyle.
2. **Bekletme:** Kullanıcı bir soru sorduğunda arka planda araç döngüsüne girip bekletme; önce kullanıcıya net yanıt ver.
3. **Game Logic Bible'a Sadık Kal:** Görsel dilde kübik (Minecraft tarzı) tasarımlara ASLA geri dönme; daima *Astroneer / Deep Rock Galactic / 7 Days to Die* low-poly faceted standardını uygula.
4. **Sıfır Hata ve Taşınabilirlik Kuralı:** Kodlarda asla `0 Error / 0 Warning` standardından taviz verme ve mutlak dosya yolu kullanma.
