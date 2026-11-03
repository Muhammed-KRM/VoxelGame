# 📌 Stratocraft — Canlı Proje Durumu ve Bilgisayarlar Arası Yapay Zeka Devir Raporu (`DURUM_VE_DEVIR.md`)

> [!IMPORTANT]
> **YAPAY ZEKA AJANI İÇİN ZORUNLU BAŞLANGIÇ TALİMATI (YENİ SOHBETE VEYA YENİ OTURUMA BAŞLARKEN):**
> 1. Bu döküman projenin tek ve güncel **Devir Teslim Raporudur**.
> 2. Yeni sohbete başlar başlamaz `git status -s` ve `git log -5 --oneline --date=iso` komutlarını çalıştırıp reponun bu dökümanla birebir eşleştiğini doğrula.
> 3. Kullanıcıya uzun uzun bekleterek işlem yapma; önce aşağıdaki **"Bölüm 3: Yarım Kalan / Kullanıcı Tarafından Henüz Test Edilmemiş Noktalar"** ve **"Bölüm 5: Sıradaki İlk İş"** adımlarını oku ve kullanıcıyla kaldığı yerden devam et.
> 4. Hiçbir dosyada `D:\oyun` gibi mutlak (hardcoded) yol kullanma, daima göreceli yol kullan.

---

## 1. Aktif Devir Kimliği (Son Durum Özeti)

| Alan | Değer |
| :--- | :--- |
| **Devir Numarası** | `#019` (Guard-Konumlu Tam-Yay Balistik Kılıç, 4 Silah × 4 Dojo Stili Matrisi, Fiziksel Balta/Kazma Savuruşu, Yere Düşen 3B Itemlar, Maddeye Özel Moloz ve `[F9]` Oyuncu Uzuv Kopması) |
| **Son Güncelleme Tarihi (ISO-8601)** | `2026-11-03T17:43:00+03:00` |
| **Ortam / Donanım** | `Windows 11`, `NVIDIA GeForce RTX 3050 Ti Laptop GPU 4GB`, `16 GB RAM`, Unity 6 `6000.6.7f1` |
| **Mevcut Çalışma Aşaması** | `Faz 1 & Faz 2 Fiziksel Simülasyon ve Dövüş/Madencilik Revizyonu Kodlandı -> Sıradaki: Kullanıcının Son Paketi Play Mode'da Test Etmesi ve İnce Ayar / Faz 3 Geçişi` |
| **Aktif Test Durumu** | `34/34 Unity EditMode PASS`, `8/8 Unity PlayMode PASS` (`Toplam 42/42 Unity Test PASS`), `Unity 6 Console: 0 Error / 0 Warning` |
| **Kaplama VRAM Bütçesi** | `4.14 MB / 16.000 MB` (`10/10` 3B Model + `4/4` SVOX Şablonu, `%100` Bütçe İçi, Hedef: `< 16.0 MB`) |
| **Çalışma Belleği (RAM)** | `< 2.5 GB` (`0 B/kare` GC Alloc) |

---

## 2. Bu Oturumda Tamamlanan Tüm İşler (Devir `#018` ve `#019`)

### 2.1. Üretilen ve Güncellenen Mimari Raporlar & Anayasa
1. **7 Mimari ve Kök Neden Raporu (`docs/`):**
   - [`docs/SERBEST_FIZIKSEL_DOVUS_SISTEMI_MIMARI_RAPORU.md`](docs/SERBEST_FIZIKSEL_DOVUS_SISTEMI_MIMARI_RAPORU.md) *(Bölüm 9–10 eklendi: Guard-Konumlu Tam-Yay Balistik Savurma, `150–200 ms` Re-Chambering, `Cleave` vs `Hilt Bash`, 4 Silah × 4 Dojo Stili Matrisi ve 3 Fiziksel Uyumsuzluk Cezası)*
   - [`docs/FIZIKSEL_ENVANTER_CANTA_VE_HOLSTER_RAPORU.md`](docs/FIZIKSEL_ENVANTER_CANTA_VE_HOLSTER_RAPORU.md)
   - [`docs/MADDE_KRISTALI_VAKUUM_VE_HIBRIT_VOKSEL_RAPORU.md`](docs/MADDE_KRISTALI_VAKUUM_VE_HIBRIT_VOKSEL_RAPORU.md)
   - [`docs/DOMINO_YIKIM_VE_PANCAKE_FIZIGI_RAPORU.md`](docs/DOMINO_YIKIM_VE_PANCAKE_FIZIGI_RAPORU.md)
   - [`docs/PROSEDUREL_IK_ANIMASYON_VE_UZUV_KOPMA_RAPORU.md`](docs/PROSEDUREL_IK_ANIMASYON_VE_UZUV_KOPMA_RAPORU.md)
   - [`docs/VOKSEL_AI_YOL_BULMA_VE_BOSS_EKOLOJISI_RAPORU.md`](docs/VOKSEL_AI_YOL_BULMA_VE_BOSS_EKOLOJISI_RAPORU.md)
   - [`docs/PLAYMODE_TEST_HATALARI_KOK_NEDEN_RAPORU.md`](docs/PLAYMODE_TEST_HATALARI_KOK_NEDEN_RAPORU.md)
   - [`docs/FAZ1_FAZ2_GERCEK_DURUM_VE_EKSIKLER_RAPORU_V2.md`](docs/FAZ1_FAZ2_GERCEK_DURUM_VE_EKSIKLER_RAPORU_V2.md)
2. **Oyun Anayasası Güncellemesi:**
   - [`STRATOCRAFT_ EVRENSEL OYUN TASARIM VE MANTIK ANAYASASI (GAME LOGIC BIBLE) (2).md`](STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md): **Bölüm 4.2.1** olarak Guard-Konumlu Tam-Yay Balistik Kılıç sistemi, 4 Silah Arketipi × 4 Dövüş Sanatı / Dojo Stili Matrisi, 3 Fiziksel Uyumsuzluk Cezası ve Fiziksel Alet (`[2]` Kazma / `[3]` Balta) Savurma + Yere Düşen 3B Fiziksel Eşya (`DroppedWorldItem`) kuralları kanonik olarak işlendi.

### 2.2. Guard-Konumlu Tam-Yay Balistik Kılıç & `4 Silah [F8] × 4 Dojo Stili [G]` (`Stage1DirectionalCombatBridge.cs`, `Stage1CreatureSimulationBridge.cs`)
- **Kılıç Ortada İleri Doğru Durmaz:** Boştayken seçili Dojo stilinin doğal **Guard** duruşunda (ör. Yüksek Şövalye Duruşunda sağ-yukarıda dik, Iaido'da sol beldeki kında) bekler.
- **Sağ Tık (`RMB`) + Fare (`Guard / Parry`):** Kılıcın Guard/Blok açısını çember üzerinde istediğin köşeye (sol-üst, sağ-üst, sağ-alt, sol-alt) yerleştirir ve orada fiziksel siper (**Parry**) tutar. Kılıç en son bıraktığın Guard köşesinde kalır.
- **Sol Tık (`LMB`) + Fare Savurması (`ActiveFullArcSwing`):** Kılıç en son hangi Guard konumundaysa **oradan başlayarak** fareyi savurduğun karşı köşeye kadar **tam-yay balistik kesik** atar (ekran ortasından yarım kesik başlamaz).
- **`0 ms` vs `150–200 ms` Fiziksel Geçiş (`Re-Chambering`):** Kılıç zaten yukarıdayken aşağı çapraz kesik atarsan **0 ms gecikmeyle** anında iner; ters köşeden kesik atmak istersen kılıç önce **`150–200 ms`** (`ReChamberingToStart`) başlangıç köşesine fiziksel geçiş yapar, ardından tam-yay kesiği indirir.
- **Canlı Eti Yarma (`Cleave`) vs Kabza Çarpması (`Hilt Bash`) vs Duvar Sekmesi:**
  - Kılıç sert taş/duvara çarparsa fiziksel olarak seker ve durur (`AbortSwingOnObstacle`).
  - Kılıç bir canlıya normal bıçak mesafesinde (`u > 0.22`) değerse **durmaz**; et direnci hissederek canlının içinden kesip geçer (`NotifyBladeCleaveThroughFlesh`), canlının gövdesinde **3B kesik yarası (`AttachSlashWoundScar`)** açar veya uzvunu koparır ve savurma yayını tamamlar.
  - Canlıya aşırı yakın kabza mesafesinde (`u <= 0.22`) çarparsa kesemez, **Kabza Darbesi (`Hilt Bash`)** vurup durur.
- **4 Silah Arketipi (`[F8]`) × 4 Dövüş Sanatı / Dojo Stili (`[G]`) ve 3 Fiziksel Uyumsuzluk Cezası:**
  - **4 Silah (`[F8]`):** `Longsword (1.85 kg)`, `Greatsword (5.60 kg)`, `Rapier (0.95 kg)`, `Katana (1.35 kg)`.
  - **4 Dojo Stili (`[G]`):** `HighGuard_VomTag`, `Iaido_QuickDraw`, `Fencing_Punta`, `TwoHand_Juggernaut`.
  - **Uyumsuz Kombinasyonun 3 Fiziksel Cezası (Örn. `Greatsword 5.60 kg` + `Fencing_Punta` / `Iaido` veya sol kolu kopuk oyuncu):**
    1. *Yerçekimi Uç Sarkması & 2.5x Yavaşlama:* Silahın ucu ağırlıktan aşağı sarkar (`tipDroopRad = -0.58 rad`), savurma 2.5x yavaşlar ve hasar `%35`'e düşer.
    2. *Savurma Sonu Savrulma (`Overswing Stagger`):* Savurma sonunda ağır kılıcın ataleti karakteri savurma yönüne doğru çeker (`26°`) ve `0.52 sn` denge kaybı yaşatır.
    3. *Guard Break & Kendine Çarpma:* Tek el stilde ağır kılıçla düşman darbesini bloklamaya (`RMB`) çalışırsan blok kırılır ve kılıç oyuncunun kendi suratına savrularak `-9 HP` geri tepme hasarı verir (`NotifyParryImpactAndCheckGuardBreak`).

### 2.3. Fiziksel Balta (`[3]`) / Kazma (`[2]`) Savuruşu ve Yere Düşen 3B Fiziksel Itemlar (`Stage1MatterCrystalAndMiningController.cs`, `StratocraftFloatingIslandWorldGen.cs`)
- **Fiziksel Alet Savurma (`[2]` Kazma & `[3]` Balta):** Artık kılıçla birebir aynı şekilde çalışır. Fareyi savurmadan sadece sol tıka basılı tutmak kazma/kesme yapmaz; fareyi savurarak baltayı ağaca veya kazmayı kayaya fiziksel olarak vurmak gerekir (`ConsumePendingToolVoxelHit` + `ActiveFullArcSwing`).
- **Madde Kristali (`[C]`):** Yanına yaklaşmadan uzaktan helisel vakum ışınıyla maddeleri kendi içine çeker.
- **Yere Düşen 3B Fiziksel Itemlar (`DroppedWorldItem`):** Balta, kazma veya çıplak elle kırılan her blok ve devrilen ağaç/yapı parçaları Minecraft/The Forest tarzı **3B fiziksel item** olarak yere fırlar, yerçekimiyle düşüp zıplar ve yanına yürüdüğünde (`<= 2.15m`) veya `[E]` tuşuyla envantere/kristale toplanır.
- **Ağaç Kesme (`BuildVoxelTree`):** Ağaç gövdelerinin etrafındaki yan toprak çıkıntıları (`dy = 1..2`) temizlendi; taban gövde bloğu baltayla kesildiğinde tüm ağaç fiziksel olarak devrilir.

### 2.4. Her Maddenin Kendi Moloz Versiyonu (`StructuralCollapseSystem.cs`, `Stage1StructuralCollapseBridge.cs`)
- `RevoxelizeClusterToLooseRubble` ve `FinalizeClusterImpact` güncellendi: Çöken hiçbir yapı veya ağaç artık tek tip gri `LooseRubble (11)` maddesine dönüşmez; her blok **kendi `MaterialId` değerini** (`Meşe Odunu Molozu`, `Demir Ağacı Molozu`, `Mermer Molozu`, `Kaya Molozu`, `Altın Cevheri Molozu` vb.) `ShapeId = 63` (`ChiseledMicroVoxelShapeId`) ile korur ve kırıldığında/vakumlandığında kendi gerçek maddesini verir.

### 2.5. Canlı ve Oyuncu Uzuv Kopması (`[F9]` Test Kısayolu - `Stage1ArticulatedRigAnimator.cs`, `Stage1InteractivePlayController.cs`)
- **Canlılarda:** Kılıçla yapılan her darbe canlının üzerinde 3B kesik yarası (`AttachSlashWoundScar`) açar; kol/kanat veya bacak bölgesine (ya da gövdeye güçlü darbe) vurulduğunda uzuv kopar (`GoreCap` + havaya fırlayan uzuv parçası).
- **Oyuncu Karakterinde (`[F9]` Tuşu):** Play Mode'da oyuncunun kendi karakterinde uzuv kopmasını anında test edebilmesi için **`[F9]`** tuşu eklendi (`CyclePlayerLimbDismembermentTest`):
  - **1. Basış (`[F9]`):** Kamera otomatik TPS'e geçer, oyuncunun **Sol Kolu kopar** (`GoreCap` + fırlayan kol + ağır kılıçlarda tek-el ağırlık cezası).
  - **2. Basış (`[F9]`):** Oyuncunun **Sağ Bacağı kopar** (`GoreCap` + fırlayan bacak + belirgin topallama yürüyüşü).
  - **3. Basış (`[F9]`):** Tüm oyuncu uzuvları iyileşir (`Shift + F9` ise `.sregion` kaydını yükler).

### 2.6. Önceki Turda Çözülen Play Mode Kök Nedenleri (`PLAYMODE_TEST_HATALARI_KOK_NEDEN_RAPORU.md`)
- **Sıvıların Akıp Yok Olması:** `StratocraftFloatingIslandWorldGen.cs` içinde gölet ve kaldera etrafına geçirimsiz taş çanak duvarı örüldü ve `FluidCellularAutomataJob.cs` içinde yanal taşma sönümlemesi eklendi.
- **Baktığın Yeri Kazamama:** Dual Surface Nets eğik üçgen normalleri (`-hit.normal`) yerine doğrudan kamera bakış ekseni (`cam.transform.forward * RayMarchOffsets`) üzerinden voksel tespiti yapıldı.
- **Rastgele Harita, Ağaçlar ve Yapılar:** Sahne chunk'ları yeni `StratocraftFloatingIslandWorldGen` (7 ağaç + Gözetleme Kulesi + Tapınak + Taş Kemer) ile senkronize edildi ve **`[F7]`** rastgele tohum (`ActiveWorldSeed`) yenileme tuşu eklendi.

---

## 3. Yarım Kalan / Kullanıcı Tarafından Henüz Test Edilmemiş Noktalar (Sonraki Oturuma Devredenler)

1. **Kullanıcının Son Paketi Play Mode'da Canlı Test Etmesi (Öncelik #1):**
   - Kullanıcı ilk düzeltme paketini test etmiş (kazma ve sıvıların düzeldiğini onaylamış), ancak konuşarak birlikte tasarladığımız **ikinci paketi** (`Guard-Konumlu Tam-Yay Kılıç`, `[G] 4 Dojo Stili × [F8] 4 Silah Arketipi`, `Fiziksel Balta/Kazma Savurma + Yere Düşen 3B Itemlar`, `Her Maddenin Kendi Molozu`, `[F9] Oyuncu Uzuv Kopması` ve daha önce test etmeye fırsat bulamadığı `[F7] Rastgele Harita`) henüz bizzat Play Mode'da oynayarak test etmedi.
   - Yeni oturumda kullanıcı Play Mode'da bu mekanikleri denedikten sonra **hissiyat ve ince ayar (tuning)** geri bildirimleri (ör. savurma ivme eşiği, `150–200 ms` Re-Chambering hızı, balta/kazma vuruş yarıçapı, Dojo duruş açıları) alınacak.
2. **İleriki Fazlara (Faz 3+) Bağlanacak Genişletmeler:**
   - **Dojo Stili Açma & Ustalık (`StanceMastery01`):** Şu an test kolaylığı için 4 Dojo stilinin tamamı açık (`AllDojoStancesUnlocked = true`). Faz 3–5 ilerlemesinde dünyadaki **Dojo Ustaları / Parşömenler** ile açılması ve kullandıkça ustalık (`0.0 -> 1.0`) kazanılması bağlanacak.
   - **Örs Üzerinde Dövülen Özel Silah Modelleri:** Şu an `[F8]` ile geçiş yapılan 4 kılıç arketipi (`Longsword`, `Greatsword`, `Rapier`, `Katana`), Faz 3'te `16x32x4` **Çift Boynuzlu Örs** üzerinde dövülen bağımsız 3B GLB modelleriyle genişletilecek.

---

## 4. Güncel Sahne ve Oynanış Tuş Kontrolleri

| Tuş / Eylem | Fonksiyon | Durum |
| :--- | :--- | :--- |
| **`RMB` (Sağ Tık) + Fare** | Kılıcın **Guard / Blok (Parry)** yönünü çember üzerinde ayarlar ve orada tutar | ✅ Aktif |
| **`LMB` (Sol Tık) + Fare Savurması** | Kılıcı/Baltayı/Kazmayı son Guard konumundan hedef yöne **Tam-Yay Balistik** savurur | ✅ Aktif |
| **`[G]` Tuşu** | **4 Dövüş Sanatı / Dojo Stili** arasında geçiş (`High Guard`, `Iaido`, `Eskrim`, `Juggernaut`) | ✅ Aktif |
| **`[F8]` Tuşu** | **4 Kılıç Arketipi** arasında geçiş (`Longsword 1.85kg`, `Greatsword 5.6kg`, `Rapier 0.95kg`, `Katana 1.35kg`) | ✅ Aktif |
| **`[F9]` Tuşu** | **Oyuncu Uzuv Kopması Canlı Testi** (`1: Sol Kol Kopar` -> `2: Sağ Bacak Kopar` -> `3: İyileşir`) \| `Shift+F9`: `.sregion` Yükle | ✅ Aktif |
| **`[F7]` Tuşu** | **Rastgele Harita Tohumu (`Seed`) Üretimi** (7 Ağaç + Kule + Tapınak + Kemer yeniden üretilir) | ✅ Aktif |
| **`[1] / [2] / [3] / [4] / [0]`** | `1`: Kılıç, `2`: Fiziksel Kazma, `3`: Fiziksel Balta, `4`: İnşa Bloğu, `0/H/Q`: Kılıfa Sok (`Boş El`) | ✅ Aktif |
| **`[X]` veya `[Shift+G]`** | Eldeki silahı/aleti `0.0s` sürede fiziksel olarak yere atar (`Quick-Drop`) | ✅ Aktif |
| **`[E]` veya Yürüyerek Yaklaşma** | Kırılan bloklardan yere düşen **3B Fiziksel Itemları** toplar / Sandık açar / Örs geri dönüşümü yapar | ✅ Aktif |
| **`[C]` Tuşu** | Sol Bilek **Madde Kristali** modunu açar/kapatır (uzaktan helisel vakum ışınıyla çeker) | ✅ Aktif |
| **`[F5]` / `[F6]`** | `F5`: FPS / TPS Kamera Değiştir \| `F6`: `.sregion` Kaydet | ✅ Aktif |

---

## 5. Sıradaki İlk İş (Yeni Oturum / Yeni Bilgisayar İçin)

1. **Adım 1 — Durumu Kontrol Et:**
   ```powershell
   git status -s
   git log -5 --oneline --date=iso
   ```
2. **Adım 2 — Kullanıcı Play Mode Geri Bildirimini Al veya Kalite Kapısı ile Faz 3'e Geç:**
   - Kullanıcıya Play Mode'da son eklenen **`[G]` Dojo Stilleri**, **`[F8]` 4 Kılıç**, **`RMB/LMB` Guard-Konumlu Tam-Yay Kesik & Cleave**, **Fiziksel Balta/Kazma + Yere Düşen 3B Itemlar**, **Maddeye Özel Moloz**, **`[F7]` Rastgele Harita** ve **`[F9]` Oyuncu Uzuv Kopması** özelliklerini test edip etmediğini sor; istediği ince ayarlar varsa uygula, yoksa `powershell -ExecutionPolicy Bypass -File scripts/quality-gate.ps1 -Phase 2` çalıştırıp **Faz 3**'e geç.
