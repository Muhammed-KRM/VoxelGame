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
| **Devir Numarası** | `#012` (FPS Viewmodel, CSG Chunk Init, Sırt Çantası Kamera Eğimi Düzeltildi, Son Commit: `0dc82cc`) |
| **Son Güncelleme Tarihi (ISO-8601)** | `2026-11-02T01:05:00+03:00` |
| **Ortam / Donanım** | `Windows 11`, `NVIDIA GeForce RTX 3050 Ti Laptop GPU 4GB`, `16 GB RAM`, Unity 6 `6000.6.7f1` |
| **Mevcut Çalışma Aşaması** | `Faz 1 ve Faz 2 Oynanış Hataları Çözüldü -> Sıradaki: C (Kristal) ve I (Çanta) Tuş Test Doğrulaması & Faz 3` |
| **Aktif Test Durumu** | `40/40 .NET 10 Core PASS`, `32/32 Unity EditMode PASS`, `9/9 Python PyTest PASS`, `6/6 C++ GoogleTest PASS`, `Unity 6 Console: 0 Error / 0 Warning` |
| **Kaplama VRAM Bütçesi** | `4.14 MB / 16.000 MB` (`%100` Bütçe İçi, Hedef: < 16.0 MB) |
| **Çalışma Belleği (RAM)** | `< 2.5 GB` |
| **Son Başarılı Commit** | `0dc82cc` (*fix: FPS viewmodel + CSG chunk init + backpack mat kamera + WorldGen namespace refactor*) |

---

## 2. Son Oturumda Yapılanlar ve Çözülen Kritik Sorunlar

1. **FPS Viewmodel ve Kamera Bakış Açısı (Bible §3.5):**
   - Karakterin elindeki kılıç ve silahlar gövdede (hip seviyesinde) kaldığı için FPS kamerasının görüş açısı (frustum) dışına düşüyordu.
   - `WeaponPivot` doğrudan `Main Camera`'ya parent olarak bağlandı ve lokal pozisyonu `(0.30, -0.24, 0.50)` olarak ayarlandı. Artık kılıç sağ alt köşede canlı görünüyor, ortada artı imleci (crosshair) bulunuyor.
   - Sahne başlangıcında kamera referansı bulunamazsa otomatik tespit veya fallback kamera oluşturma mekanizması eklendi.

2. **Sol Bilek Madde Kristali CSG Kazı & İnşa (Bible §3.1-§3.2):**
   - Play Mode'a girildiğinde `_worldChunks` sözlüğü Unity sahne serileştirmesi nedeniyle boşalıyordu ve kazı ışını hiçbir voksile temas edemiyordu.
   - `Stage1InteractivePlayController.cs` içine `EnsureChunksInitialized()` entegre edildi. Sahnede bulunan 16 chunk runtime başlangıcında kaydedilerek CSG kazı ve inşa işlemlerine hazır hale getirildi.

3. **Fiziksel Sırt Çantası Örtüsü (Bible §3.3-§3.4 & §6):**
   - 'I' tuşuna basıldığında örtü `y=0.03` seviyesinde yerde açılıyor fakat kamera ufka baktığı için görünmüyordu.
   - Envanter açıldığında kamera ~52° aşağı eğilecek şekilde yönlendirildi.
   - `StratocraftStage1SceneBuilder.cs` içindeki `BoxCollider` ekleme mantığı temizlendi ve sağlamlaştırıldı.

4. **Namespace ve Proje Mimarisi Refactor:**
   - `StratocraftFloatingIslandWorldGen.cs` dosyası `Assets/_Stratocraft/WorldGen/` altına taşındı, isim alanı `Stratocraft.WorldGen.Generators` yapıldı.
   - Unity C# projesinde `MathF.Min` kaynaklı tür uyuşmazlığı giderildi (`Math.Min` kullanıldı).
   - `Stratocraft.Character.asmdef` dosyasına `Stratocraft.WorldGen` derleme referansı eklendi.
   - Standalone testler ve Unity EditMode testleri sıfır hata ile derlendi ve yeşil geçti.

---

## 3. Sahne ve Oynanış Durumu (Tuş Kontrolleri)

| Tuş / Eylem | Fonksiyon | Durum |
| :--- | :--- | :--- |
| **Mouse Hareketi** | FPS Bakış Yönü | ✅ Aktif (Cursor kilitli) |
| **WASD** | Hareket + Head Bobbing + Prosedürel Salınım | ✅ Aktif |
| **Left Shift** | Depar (Sprint) | ✅ Aktif |
| **Left Ctrl + Sprint** | Kayma (Slide) | ✅ Aktif |
| **Z** | Yüzüstü Sürünme (Prone - 0.65m profil) | ✅ Aktif |
| **Space** | Zıplama / 2.2m Kenar Tırmanışı (Ledge Mantle) | ✅ Aktif |
| **F5** | FPS / TPS Kamera Açısı Değiştirme | ✅ Aktif (Varsayılan: FPS) |
| **Esc** | Mouse İmleç Kilidini Aç/Kapat | ✅ Aktif |
| **C** | Madde Kristali Aktif/Pasif (Sol Bilek) | ⚠️ Kodlandı (Doğrulama bekliyor) |
| **LMB (Sol Tık)** | Voksel Kazı (CSG Vakum) / Saldırı | ⚠️ Kodlandı (Doğrulama bekliyor) |
| **RMB (Sağ Tık)** | Voksel İnşa (Madde Püskürtme) / Blok | ⚠️ Kodlandı (Doğrulama bekliyor) |
| **I** | Sırt Çantası Kumaş Matını Yere Ser (Envanter) | ⚠️ Kodlandı (Kamera eğimi eklendi) |
| **T** | İnşaat / Blok 90° Döndürme | ✅ Aktif |

---

## 4. Sıradaki İlk İş (Yeni Oturum / Yeni Bilgisayar İçin)

Yeni oturumda çalışacak yapay zeka ajanı doğrudan şu sırayı takip etmelidir:

1. **Adım 1 — Repoyu ve Derlemeyi Doğrula:**
   ```powershell
   git status -s
   git log -1 --oneline
   dotnet test Tests/Stratocraft.Tests.csproj -c Release
   ```
2. **Adım 2 — Canlı Oynanış Doğrulaması:**
   - Unity Editor üzerinde `Assets/Scenes/StratocraftStage1Showcase.unity` sahnesini aç.
   - Play Mode'a gir.
   - Konsolda 0 Error olduğunu doğrula.
   - 'C' tuşuna basarak Sol Bilek Kristalinin devreye girdiğini (HUD'da gösterge ve model) doğrula.
   - 'I' tuşuna basarak kameranın yere eğildiğini ve serilen çantanın göründüğünü doğrula.
3. **Adım 3 — Faz 3 Başlangıcı (Zanaat, İnşaat & Klan Alanı):**
   - **Faz 3.1:** Çift boynuzlu örs mini-voksel çekiçleme ve külçe dövme.
   - **Faz 3.2:** 7DTD tarzı hayalet blok (ghost preview) ve hologram inşaat.
   - **Faz 3.3:** 3B Çit BFS Klan Koruma Alanı hesaplaması.

---

## 5. Başka Bilgisayara Geçiş Rehberi

Projeyi başka bir bilgisayara aktarırken:
1. `git clone` veya mevcut klasörü yeni bilgisayara kopyalayın.
2. Unity Hub üzerinden **Unity 6 (`6000.6.7f1`)** ile projeyi açın.
3. Açılışta Asset Import işleminin bitmesini bekleyin.
4. Konsolda derleme hatası olmadığını doğrulayın.
5. Üst menüden `Araçlar > Stratocraft > Aşama 1 - Canlı Dünya ve 3B Varlık Sahnesini Kur` seçeneğine tıklayarak sahneyi tazeleyin ve Play'e basın.
