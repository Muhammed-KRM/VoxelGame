# Stratocraft Calisma Alani Kurallari (Antigravity Workspace Rules)

1. **Faz ve Oturum Oncesi Devir Kontrolu (ZORUNLU):**
   - Her faz veya oturum basinda once kok dizindeki `DURUM_VE_DEVIR.md` dosyasini oku ve `git log -5 --oneline --date=iso` ile karsilastir.
   - Rapor son commit'ler ile eslesiyorsa **YENI (GUNCEL)**, eski bir fazda kalmissa **ESKI (BAYAT)** oldugunu tespit et ve kodlamaya baslamadan once kullaniciya sorup onay al.
2. **Iki Bilgisayarli (Sabah Is PC <-> Aksam Ev PC) Tasinabilirlik:**
   - Hicbir kod, test veya `.ps1` betigi icinde `D:\oyun` veya kullanici adina bagli sabit (hardcoded) mutlak yol kullanma; daima `$PSScriptRoot` / goreceli yollar kullan.
   - Tum `.ps1` PowerShell betiklerini saf ASCII karakterlerle yaz (Windows PowerShell 5.1 UTF-8 BOM'suz okuma uyumlulugu icin).
3. **Sifir Elle Modelleme ve Performans Butceleri:**
   - Hicbir 3B model/doku/animasyon kullanicidan elle istenmez; tamami `Tools/AutonomousAssetFactory/` uzerinden uretilir ve `asset_validator.py` + 4 acili PNG ile dogrulanir.
   - Kaplama VRAM butcesi `< 16.0 MB`, Oyun RAM butcesi `< 2.500 MB`, GC Alloc `0 B/kare`, Derleme `0 Error / 0 Warning`.
4. **Kalite Kapisi:**
   - Faz bitiminde `powershell -ExecutionPolicy Bypass -File scripts/quality-gate.ps1 -Phase <N>` calistirilmadan ve `docs/test-raporlari/faz<N>-raporu.md` + `DURUM_VE_DEVIR.md` guncellenmeden sonraki faza gecilmez.
5. **Seffaf Adim Adim Ilerleme Bildirimi (ZORUNLU):**
   - Herhangi bir arastirma, hata ayiklama (debug) veya ozellik gelistirme sirasinda, her adimda hangi dosyaya/fonksiyona neden baktigini ve o anda ne yaptigini kullaniciya kisaca raporla ("Sunun nasil calistigini anlamak icin suraya bakiyorum / Simdi su adimi yapiyorum" seklinde).
6. **Buyuk Ozellik Gelistirme Oncesi 4'lu Dokumantasyon Standardi:**
   - Kullanici buyuk bir ozellik gelistirmeden once dokuman hazirlanmasini istediginde (kullanici acikca farkli bir format veya tek bir dokuman istemedigi surece) su 4 dokumani/bolumu eksiksiz hazirla:
     1. **Hedef ve Gereksinim Dokumani (Biz Ne Yapmak Istiyoruz?):** Gelistirilecek ozelligin amaci, beklenen davranislari, kullanici deneyimi ve tasarim/is kurallari.
     2. **Mevcut Kod Debug ve Akis Raporu (Su Anki Sistem Nasil Calisiyor?):** Mevcut kodun adim adim nasil calistigi, hangi fonksiyonun hangi sirayla cagrildigi, hangi degiskene ne atandigi, veri akisi ve mevcut sorunlarin/kisitlarin tam teknik analizi.
     3. **Endustri, Benzer Projeler ve Literatur Arastirmasi (Baskalari Nasil Yapmis?):** Diger projelerde, acik kaynak sistemlerde ve sektorde bu problemin hangi algoritmalar, mimariler, matematiksel modeller ve teknolojilerle cozuldugunun karsilastirmali analizi.
     4. **Adim Adim Gelistirme Plani (Nasil Gelistirecegiz?):** 1., 2. ve 3. dokumanlara referans vererek; kullanilacak teknolojileri, degisecek dosyalari ve adim adim uygulama asamalari anlatan teknik gelistirme dokumani.
   - **Dikkat:** Kullanici ozel olarak farkli bir dokuman turu veya sadece belirli bir raporu isterse bu 4'lu kalibi zorlama; kullanicinin o anki istegine gore esnek davran.
