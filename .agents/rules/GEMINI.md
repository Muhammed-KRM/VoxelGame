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
