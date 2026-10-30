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
| **Devir Numarası** | `#010` (Faz 1 & 2 Tamamlandı, Kodlar Commitlendi, Son Durum ve Devir Detayları İşlendi) |
| **Son Güncelleme Tarihi (ISO-8601)** | `2026-10-30T17:58:00+03:00` |
| **Ortam / Donanım** | `EV-PC` (`Windows 11 Pro`, `NVIDIA GeForce GTX 1650 4GB`, `16 GB RAM`, Unity 6 `6000.6.7f1`) |
| **Mevcut Çalışma Aşaması** | `Faz 1 & Faz 2 Kodları Tamamlandı -> Sıradaki: Çimen/Yüzey Rengi Teyidi, Faz 2 Kalite Kapısı ve Faz 3 Başlangıcı` |
| **Son Git Commit (`HEAD`)** | `f6f3a7b feat(faz1-faz2): URP 17 Forward+, 0.000m dikissiz 3B ada mesher, Blender 4.2 GLB fabrikasi ve fiziksel kontrolcu tamamlandi` |
| **Aktif Test Durumu** | `25/25 C# NUnit PASS`, `9/9 Python PyTest PASS`, `6/6 C++ GoogleTest PASS`, `Unity 6 Console: 0 Error / 0 Warning` |
| **Kaplama VRAM Bütçesi** | `3.382 MB + 0.512 MB MicroDetailArray = ~3.89 MB < 16.0 MB` (`%100` Bütçe İçi) |

---

## 2. En Son Neyle Uğraşılıyordu? (Yarım Kalan / Detaylandırılan Kısım)

En son yapılan çalışma bir kod, derleme ya da mekanik hatası **değildir**. Tüm kodlar derlenmekte, testler %100 geçmekte ve Unity'de 0 hata/uyarı bulunmaktadır.

Uğraşılan son konu **küçük bir görsel yüzey rengi eşleşmesidir**:
1. **Mevcut Görsel Durum:**
   - Adanın 3B kalın hacmi, dik falezleri, alt bazalt omurgası, `0.000m` sıfır çatlaklı dikişsizliği başarıyla oluştu.
   - URP 17 Forward+ yumuşak gölgeleri, noktasal atmosfer ışıkları (lav kalderası, örs ocağı, klan kristali) aktifleşti.
   - Headless Blender 4.2 ile yontulan tüm modeller (kanatlı Anka Kuşu, iki ayaklı Ash Stalker sürüleri, mancınık, çift boynuzlu örs istasyonu, sandık, klan kristali, 15 parça zırh) sahnede düzgün render ediliyor.
2. **İncelenen Renk Detayı:**
   - Adanın üst platosundaki çimen blokları (`MaterialID = 3 Grass`), `T_Stratocraft_MaterialLUT_256x16.png` tablosundan okunurken shader'da zümrüt yeşili yerine **açık gri/taş tonunda** görünüyordu.
   - Bunun sebebi: Mesher sıfır-GC `AllocateWritableMeshData` kullanırken vertex color `UNorm8` kanalı yazıyor. Shader'daki `LoadMaterialLutExact` fonksiyonunda Unity'nin yükleme esnasındaki dikey ters çevirme (`flippedY = 15 - rowIdx`) formülü ile materyal ID'sinin satır eşleşmesi inceleniyordu.
   - Shader'da gerekli düzenleme yapıldı ve commit'e dahil edildi.

---

## 3. Neler Kaldı? (Açık Kalan Maddeler)

1. **Görsel Kontrol:** Adanın üst platosundaki çimenlerin zümrüt yeşili tonunun sahnede kullanıcıya gösterilerek teyit edilmesi.
2. **Faz 2 Resmi Kalite Kapısı:** `powershell -ExecutionPolicy Bypass -File scripts/quality-gate.ps1 -Phase 2` çalıştırılarak resmi test raporunun (`docs/test-raporlari/faz2-raporu.md`) üretilmesi.
3. **Faz 3 Geliştirmesi:** Kullanıcı onayının ardından Faz 3 (Temel Üretim, Hologram İnşa, 3D Çit BFS Klan Koruma Alanı ve KGP Kristalleri) geliştirmesine geçilmesi.

---

## 4. Yeni Sohbette / Oturumda Yapay Zekanın Sıradaki İlk İşi

Yeni oturumu açan ajan sırasıyla şu adımları izleyecektir:

1. **Adım 1 — Doğrulama:**
   - `git status -s` ve `git log -2 --oneline` çalıştırarak temiz çalışma ağacında olduğunu doğrula.
2. **Adım 2 — Yüzey Rengi Hızlı Teyidi (Gerekirse):**
   - Unity'de `Stratocraft/Asama 1 - Canli Dunya ve 3B Varlik Sahnesini Kur` menüsünü tetikle.
   - Game View görüntüsü alarak adanın çimen/su/lav renklerinin oturduğunu doğrula. Eğer çimen rengi gri kalmışsa `StratocraftUberShader.hlsl` içerisindeki `LoadMaterialLutExact` veya `UberMeshVertex` struct'ının renk aktarımına küçük bir göz at.
3. **Adım 3 — Faz 2 Kalite Kapısını Çalıştır:**
   - Terminalde `powershell -ExecutionPolicy Bypass -File scripts/quality-gate.ps1 -Phase 2` komutunu çalıştır.
   - Tüm testlerin (ctest, pytest, dotnet test, VRAM) %100 geçtiğini raporda belgele.
4. **Adım 4 — Kullanıcı Onayı ve Faz 3'e Geçiş:**
   - Kullanıcıya durumu kısaca özetle ve doğrudan **Faz 3 (Temel Üretim, Hologram İnşa, 3D Çit BFS Klan Koruma Alanı)** kodlamasına başla.

---

## 5. Kullanıcının Özel Çalışma ve İletişim Tercihleri

1. **Doğrudan ve Şeffaf Ol, Bekletme:** Kullanıcı zamanı kısıtlı olduğunda veya soru sorduğunda arka planda gereksiz görsel ince ayar döngüsüne girip bekletme; istenen işlemi hızla tamamla ve doğrudan net açıkla.
2. **Game Logic Bible'a Sadık Kal:** Doğal arazide kübik Minecraft tarzına asla dönme; *Astroneer / Deep Rock Galactic / 7 Days to Die* low-poly fasetli standardını koru.
3. **Sıfır Hata ve Taşınabilirlik:** `0 Error / 0 Warning` kuralından taviz verme ve mutlak dosya yolu (`D:\oyun` vb.) asla kullanma.
