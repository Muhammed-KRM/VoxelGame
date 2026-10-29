# 🔄 Stratocraft — Bilgisayarlar Arası Geçiş ve İki Makineden (İş ↔ Ev) Eşzamanlı Geliştirme Rehberi

> **Sürüm:** 1.0.0  
> **Son Güncelleme:** 29.10.2026  
> **Uzak Depo (`origin`):** `https://github.com/Muhammed-KRM/VoxelGame.git`  
> **İlişkili Belgeler:**
> - Ana Geliştirici Anayasası: [STRATOCRAFT_GELISTIRICI_DOKUMANI.md](./STRATOCRAFT_GELISTIRICI_DOKUMANI.md)
> - Canlı Ortak Yapay Zeka Hafızası ve Devir Raporu: [DURUM_VE_DEVIR.md](./DURUM_VE_DEVIR.md)

---

## 1. Bu Rehberin Amacı

Bu doküman, Stratocraft projesini **iki farklı bilgisayardan** (örneğin **Sabah: İş Bilgisayarı** ↔ **Akşam: Ev Bilgisayarı**) GitHub ve **Google Antigravity** yapay zeka ajanı üzerinden kesintisiz geliştirmek için hazırlanmıştır.

İki farklı bilgisayarda Unity 6 + C++20 SIMD + Python 3.11 + Blender 4.2 projesi geliştirirken normalde şu 4 sorun yaşanır:
1. **Sürüm Farkı Nedeniyle `15 Dakikalık` Unity `Library` Yeniden Derlemesi:** İki bilgisayarda farklı Unity alt sürümü kurulursa her geçişte tüm proje baştan import edilir.
2. **Sabit Klasör Yolu Çakışması:** Bir bilgisayarda proje `D:\oyun`, diğerinde `C:\Projeler\VoxelGame` veya farklı Windows kullanıcı adında olduğunda sabit yollar kırılır.
3. **Yapay Zeka (Antigravity) Sohbet Geçmişinin Yerel Kalması:** Antigravity'deki sohbet geçmişi yerel makinededir; diğer bilgisayara geçildiğinde oradaki yapay zeka nerede kalındığını bilmez.
4. **Derlenmiş Dosyalar (`.dll`, `.venv`) ve GitHub `1 GB` Ücretsiz LFS Kotası:** Ağır 3B modeller (`.glb`, `.exr`, `.dds`) doğrudan GitHub LFS'e atılırsa ücretsiz `1 GB` kotası hızla dolar; `.gitignore` içindeki `StratocraftNative.dll` ve `.env` ise diğer makinede eksik kalır.

Aşağıdaki **4 Altın Kural** ve **Sabah/Akşam Devir Protokolü** bu 4 sorunu `%100` ortadan kaldırır.

---

## 2. İki Bilgisayarlı Geliştirmenin 4 Altın Kuralı

### Kural 1 — Kesin Sürüm Kilidi (`scripts/setup-environment.ps1`)
Her iki bilgisayarda da kurulum elle farklı sürümler indirilerek değil, Faz 0'da oluşturulan **`scripts/setup-environment.ps1`** betiğiyle yapılır. Böylece iki makinedeki ana araç sürümleri birebir kilitlenir:
- **Unity 6 LTS:** `6000.0.25f1` (`changeset: b42f8f8282b9`) + `Windows IL2CPP` + `Dedicated Server`
- **C++20 Araç Zinciri:** `VS 2022 Build Tools (MSVC v143)` + `CMake 3.28+` + `Ninja 1.11+` + `LLVM/Clang 17+`
- **Python & 3B Fabrika:** `Python 3.11 (64-bit)` + `uv` + `Blender 4.2 LTS` + `Blockbench 4.10+`

### Kural 2 — `%100` Göreli Yol (`Relative Path`) Yasası
Hiçbir C#, C++20, Python, CMake veya PowerShell dosyasında `D:\oyun` ya da `C:\Users\...` gibi mutlak yol yazılmaz. Tüm betikler `$PSScriptRoot` veya proje köküne göreli yollarla çalışır. Projeyi Ev PC'de `D:\oyun`, İş PC'de `C:\Work\VoxelGame` altına klonlayabilirsiniz.

### Kural 3 — Hafif Git + Deterministik Yerel Üretim (`LFS Kotası Koruması`)
- GitHub'a **yalnızca kaynak kodlar, manifesto dosyaları, 3B üretim betikleri ve dokümanlar** gönderilir.
- `.env`, Python `.venv/`, C++ `build/`, `StratocraftNative.dll`, Unity `Library/` ve üretilen ağır ikili varlıklar (`Assets/Art/Generated/` altındaki büyük `.glb`, `.dds`, `.exr` çıktıları) GitHub LFS kotasını tüketmez.
- Bilgisayara oturduğunuzda çalışan **`scripts/sync-pc.ps1 -Pull`** betiği:
  1. `.env` yoksa yerel olarak otomatik üretir (`scripts/generate-secrets.ps1`),
  2. `NativePlugins/StratocraftNative` kaynak kodları değiştiyse `StratocraftNative.dll`'i o bilgisayarda birkaç saniyede derler (`scripts/build-native.ps1`),
  3. 3B varlık manifestosu değiştiyse eksik varlıkları sabit `seed` ile birebir aynı şekilde yerel üretir (`scripts/generate-assets.ps1`).

### Kural 4 — Ortak Yapay Zeka Hafızası (`DURUM_VE_DEVIR.md`) ve Tazelik Kontrolü
- Kök dizindeki **[DURUM_VE_DEVIR.md](./DURUM_VE_DEVIR.md)** dosyası iki bilgisayardaki Antigravity ajanlarının ortak beynidir.
- Yapay zeka her yeni oturumun ve **her fazın başında** önce `DURUM_VE_DEVIR.md` dosyasını okur, Git geçmişiyle (`git log -1`) karşılaştırarak raporun **YENİ (Güncel)** mi yoksa **ESKİ (Bayat)** mi olduğunu tespit eder ve **kullanıcıya sorup onay aldıktan sonra** ilerler.

---

## 3. İkinci (Yeni) Bilgisayarda İlk Gün Kurulumu (Sadece 1 Kez Yapılır)

Henüz kurulum yapılmamış diğer bilgisayara (örneğin İş Bilgisayarına) ilk kez oturduğunuzda yalnızca şu 4 adımı uygulayın:

### Adım 1 — Google Antigravity ve Git Hazırlığı
1. **Google Antigravity Desktop App** uygulamasını açıp giriş yapın; kesintisiz çalışma için ayarlardan **Turbo Mod** (`Permission Preset: TURBO`, `Auto Execution: EAGER`) seçeneğini açın.
2. İstediğiniz bir diskte terminal (PowerShell) açıp projeyi klonlayın:
   ```powershell
   git clone https://github.com/Muhammed-KRM/VoxelGame.git
   cd VoxelGame
   ```
   *(İlk seferde GitHub giriş penceresi açılırsa onaylayın).*

### Adım 2 — Otomatik Ortam Kurulumu
Antigravity içinde klonladığınız `VoxelGame` klasörünü açın ve sohbete şunu yazın *(veya Faz 0 tamamlandıktan sonra doğrudan `pwsh -File scripts/setup-environment.ps1` çalıştırın)*:
> *"Bu benim ikinci bilgisayarım. `BILGISAYARLAR_ARASI_GECIS_REHBERI.md` ve `DURUM_VE_DEVIR.md` dosyalarını oku, eksik kurulumları birebir aynı sürümlerle kur ve son durumu bana raporla."*

### Adım 3 — Yalnızca 2 Kısa Manuel Onay
1. **Unity Hub** kurulduğunda açıp Unity ID hesabınızla giriş yapın ve ücretsiz **Personal** lisansını etkinleştirin *(Ardından AI, Unity `6000.0.25f1` editörünü otomatik indirir)*.
2. Unity 6 açıldığında üst menüden **`Window > Unity MCP`** penceresinde bağlantıyı bir kez başlatın (`Start Server / Auto-Connect`).

---

## 4. Günlük Çalışma Rutini (Sabah İş ↔ Akşam Ev)

### A. Bilgisayara Oturduğunuzda (Devralma — Sabah veya Akşam Başlangıcı)

Diğer bilgisayardan yaptığınız işi çekip devam etmek için Antigravity sohbetine sadece şunu yazmanız yeterlidir:

> **"GitHub'dan son değişiklikleri çek, `DURUM_VE_DEVIR.md` devir raporunu kontrol et ve kaldığımız yerden devam etmek için bana bilgi ver."**

**Yapay Zekanın Otomatik Yapacağı İşlemler (`scripts/sync-pc.ps1 -Pull`):**
1. `git fetch origin` ve `git status -sb` ile yerel/uzak durumu kontrol eder.
2. Yerelde çakışan kirli dosya yoksa `git pull --rebase origin main` ile son kodları ve güncel `DURUM_VE_DEVIR.md` dosyasını çeker.
3. `.env` eksikse oluşturur; C++ kodu veya Python paketleri değiştiyse yerel `.dll` / `.venv` / üretilmiş 3B varlıkları günceller.
4. **`DURUM_VE_DEVIR.md` Tazelik Kontrolü:** Raporun tarihini ve baz aldığı commit'i `git log -1` ile karşılaştırır; raporun **YENİ (Güncel)** mi **ESKİ (Geride Kalmış)** mi olduğunu size bildirip *"Devir raporu güncel, şu adımda kalmışız; buradan devam edelim mi?"* diye sorar.

---

### B. Bilgisayardan Kalkarken (Devretme — Mesai Bitimi veya Gece Kapanışı)

Bir fazın ortasında (yarım kalmış iş — `WIP`) olsanız bile bilgisayarı kapatmadan önce Antigravity'ye şunu yazın:

> **"Diğer bilgisayara geçeceğim; `DURUM_VE_DEVIR.md` devir raporunu güncelle ve mevcut durumu GitHub'a gönder."**

**Yapay Zekanın Otomatik Yapacağı İşlemler (`scripts/sync-pc.ps1 -Push`):**
1. `.env`, `saves/` veya geçici dosyaların yanlışlıkla Git'e eklenmediğini doğrular.
2. **[DURUM_VE_DEVIR.md](./DURUM_VE_DEVIR.md)** dosyasını günceller:
   - Tarih/saat, bilgisayar adı (`EV-PC` veya `IS-PC`), aktif faz/adım, tamamlanan dosyalar, yarım kalan (`WIP`) satırlar, test durumu ve diğer bilgisayarda ilk yapılacak adım net şekilde yazılır.
3. Commit ve Push:
   - **Faz tamamen bittiyse:** `scripts/quality-gate.ps1` çalıştırılır, `docs/test-raporlari/fazX-raporu.md` yazılır, `feat(fazX): ...` commit'i ve `v0.X.0-fazX` etiketiyle pushlanır.
   - **Faz henüz yarım kaldıysa (`WIP`):** `wip(fazX): <kısa özet> [devir]` mesajıyla commitlenip `git push origin main` ile GitHub'a gönderilir.

---

## 5. Yapay Zeka İçin Zorunlu `DURUM_VE_DEVIR.md` Tazelik (`YENİ` vs `ESKİ`) Karar Tablosu

Yapay zeka ajanı **her yeni oturum başında ve her yeni faza (`Faz 0 → Faz 10`) geçmeden önce** aşağıdaki tabloya göre devir raporunu analiz etmek ve kullanıcıya sormak **zorundadır**:

| Tespit Edilen Durum | Nasıl Anlaşılır? | Yapay Zekanın Zorunlu Davranışı |
| :--- | :--- | :--- |
| **1. RAPOR YENİ (GÜNCEL)** | `DURUM_VE_DEVIR.md` en son commit'te (veya çalışma dizininde en son) güncellenmiş; içindeki faz/adım diskteki dosyalarla birebir uyumlu. | Rapor özetini kullanıcıya sunar: *"Güncel bir devir raporu buldum (`<tarih>`, `<PC>`). `<Faz/Adım>` noktasında kalmışız ve şu özel notlar var: `...`. Bunu dikkate alarak devam edeyim mi?"* Onay gelince rapordaki tüm notları uygulayarak devam eder. |
| **2. RAPOR ESKİ (BAYAT / GERİDE KALMIŞ)** | `DURUM_VE_DEVIR.md` güncellendikten sonra repoya yeni kod/faz commit'leri atılmış ama devir dosyası güncellenmemiş veya diskte daha ileri bir `fazX-raporu.md` var. | Kullanıcıyı uyarır: *"Bir devir raporu var ancak **ESKİ** görünüyor (rapordan sonra `<N>` yeni commit atılmış, son commit: `<mesaj>`). Eski raporu yok sayıp mevcut kod durumuna göre raporu güncelleyerek devam edeyim mi?"* |
| **3. GITHUB'DA ÇEKİLMEMİŞ DAHA YENİ RAPOR VAR** | `git fetch origin` sonrasında `origin/main` dalı yerel `main`'in ilerisinde (`behind N commits`). | Koda dokunmadan önce kullanıcıya bildirir: *"Diğer bilgisayardan GitHub'a gönderilmiş `<N>` yeni commit ve daha yeni bir devir durumu var. Önce `git pull` ile onları çekeyim mi?"* |
| **4. YERELDE GÖNDERİLMEMİŞ DEĞİŞİKLİK VAR** | `git status` çıktısında commitlenmemiş dosyalar var ve aynı anda `origin/main` de ilerlemiş. | Veri kaybını önlemek için otomatik üzerine yazmaz; yerel değişiklikleri ve uzak değişiklikleri listeler, `git stash` + `git pull --rebase` yapıp birleştirmek için kullanıcıdan onay alır. |

---

## 6. Oyun İçi Dünya Kayıtlarını (`saves/`) Taşıma (İsteğe Bağlı)

- **Normal Geliştirme Sırasında Gerekmez:** Kod yazarken ve birim/entegrasyon testlerini çalıştırırken `saves/` klasörünü taşımanıza gerek yoktur; testler deterministik `seed` ile dünyayı saniyeler içinde üretir.
- **Kendi Test Dünyanızı Taşımak İsterseniz:** Oynadığınız bir test dünyasını (`saves/<WorldName>/` ve `world_state.db` SQLite veritabanını) diğer bilgisayara götürmek istediğinizde:
  1. Bilgisayar A'da: `pwsh -File scripts/backup.ps1` çalıştırın (`backups/stratocraft_backup_<tarih>.zip` üretilir; `sqlite3 .backup` ile WAL kilidi bozulmadan güvenli yedek alır).
  2. Bu tek `.zip` dosyasını USB/Drive ile Bilgisayar B'ye kopyalayıp `pwsh -File scripts/restore.ps1 -Archive backups/stratocraft_backup_<tarih>.zip` komutunu çalıştırın.

---

## 7. Donanım Farkı Yönetimi ve Sorun Giderme

| Senaryo / Sorun | Çözüm |
| :--- | :--- |
| **Ev PC'de Harici GPU (`GTX 1650`), İş PC'de Dahili GPU (`Intel/AMD iGPU`) veya Farklı RAM Varsa** | Sorun çıkmaz. Projemiz `8 GB RAM + iGPU` tabanına göre tasarlanmıştır. Otonom 3B Fabrika her iki bilgisayarda da deterministik **Yol A (`Headless Blender 4.2 + Blockbench + MidVoxIO`)** hattını kullanır; oyun içinde ise `LowSpecGraphicsScaler.cs` o anki bilgisayarın RAM/GPU kapasitesine göre ayarları otomatik ölçekler. |
| **Diğer Bilgisayarda Push Yapmayı Unuttum!** | Yeni bilgisayarda yeni bir dosyada çalışıyorsanız devam edebilirsiniz; eski bilgisayara döndüğünüzde `git stash` -> `git pull --rebase origin main` -> `git stash pop` ile iki işi birleştiririz. Çakışma olmaması için bilgisayardan kalkarken mutlaka devir push'u yapın. |
| **`DllNotFoundException: StratocraftNative` Hatası** | Yeni bilgisayarda C++ kodu çekilmiş ama henüz derlenmemiştir. `pwsh -File scripts/build-native.ps1` (veya `baslat.ps1`) çalıştırıldığında `StratocraftNative.dll` otomatik derlenip `Assets/Plugins/x86_64/` altına kopyalanır. |
| **Unity Açılırken Farklı Sürüm Uyarısı Verirse** | **"Open Anyway" (Yine de Aç) butonuna BASMAYIN!** İptal edip `ProjectSettings/ProjectVersion.txt` içindeki tam sürümü (`6000.0.25f1`) Unity Hub üzerinden açın; böylece iki bilgisayar arasında sürüm kayması yaşanmaz. |
