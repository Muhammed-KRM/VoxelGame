# DOK3 — Endüstri, Benzer Projeler ve Literatür Araştırması (Başkaları Nasıl Yapmış?)

> **Doküman Seti Navigasyonu:**
> [DOK1: Hedef ve Gereksinimler](DOK1_HEDEF_VE_GEREKSINIMLER.md) | [DOK2: Mevcut Kod Debug & Ekran Görüntüsü Analizi](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md) | **[DOK3: Endüstri & Literatür Araştırması](DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md)** | [DOK4: Adım Adım Geliştirme Planı](DOK4_ADIM_ADIM_GELISTIRME_PLANI.md)

---

## 1. Yönlü Yakın Dövüş Fiziği ve Biyomekanik (Mordhau, Chivalry 2, Kingdom Come, Half Sword)

### 1.1 Sektör Karşılaştırması

| Özellik | Mordhau | Chivalry 2 | Kingdom Come: Deliverance | Half Sword |
| :--- | :--- | :--- | :--- | :--- |
| **Giriş Modeli** | 240° yönlü fare + tuşlar | 4 yön (Slash, Overhead, Stab, Special) | 5 yönlü HEMA yıldızı + Saplama | Doğrudan fiziksel el/bilek torku |
| **Zaman Fazları** | Kesin `Windup -> Release -> Recovery` FSM | Kesin `Windup -> Release -> Recovery` | Duruş geçişi + Master Strike | Sürekli aktif ragdoll + eklem limitleri |
| **Kamera / Turn Cap** | Silah ağırlığına göre sert açısal hız limiti (`deg/s`) | Yumuşak eğrili Turn Cap (360° helikopter engeli) | Hedefe yumuşak kilit, vahşi fare hareketi yasak | Gövde–omuz tork limiti |
| **Hareket & Ağırlık Etkisi** | Koşu/adım ivmesi darbe menzilini büyütür; ağır silahta yavaş dönüş | Ağırlık eğrisi savurma taahhüdünü artırır | Duruş değişimi namlunun fiziksel yolundan geçer | Tam atalet momenti $I = \frac{1}{3} m L^2$ + kol eklem sınırı |
| **Saplama (Thrust)** | Ayrık balistik saplama evresi (hedef başı 1 vuruş) | Ayrık balistik saplama + geri çekme | Merkez tek hamle | İleri el itmesi |

### 1.2 Stratocraft İçin Çıkarımlar ve Matematiksel Model
* **Sert Açısal Sınır (Turn Cap + Kümülatif Çekme Limiti):** *Mordhau* ve *Chivalry 2*'deki gibi savurma sırasında canlı fare yön saptırması kaldırılır; onun yerine savurma yönü `Windup -> Release` geçişinde kilitlenir. Ağır silah (`Greatsword` vb.) kamerayı ve gövdeyi savurma yönüne hafifçe çekebilir ama toplam çekme açısı sert sınırla (örn. `<= 8°` hafif, `<= 14°` ağır kılıç, `0°/s` hayalet kayma) sınırlandırılır.
* **Sabit 240 Hz Yarı-Örtük Euler PD Çözücü (`Semi-Implicit Euler`, `Lerp` Ezmesi Yok):**
  $$\tau = K_p (\theta_{\text{hedef}} - \theta) - K_d \omega - c_{\text{aero}} |\omega|\omega, \quad \omega_{n+1} = \omega_n + \frac{\tau}{I_{\text{silah}}} \Delta t_{240}, \quad \theta_{n+1} = \theta_n + \omega_{n+1} \Delta t_{240}$$
  Burada $I_{\text{silah}} = I_{\text{taban}}(m, L) \cdot k_{\text{tutuş}}(1\text{H}/2\text{H}) \cdot k_{\text{hareket}}(\text{duruş/hız})$ olarak hesaplanır; sabit $\Delta t_{240} = \frac{1}{240}\,\text{s}$ akümülatörle yürütülür ve asla `Lerp(0.65f)` ile ezilmez.
* **Anatomik Omuz–Dirsek–Bilek Zinciri (Analitik 2-Kemik IK):** Kılıcın kabzası omuz pivotundan maksimum kol erişim küresi ($R_{\max} = L_{\text{üst}} + L_{\text{ön}} - \epsilon$) içine kıstırılır. Böylece kılıç ne oyuncunun etrafında havada yörüngeye girer ne de katı düz bir çubuk gibi kayar; hem FPS hem TPS karakter modelinde 1H (sağ omuz-dirsek-bilek) ve 2H (sol el kabza altında ikincil tutuş) anatomik olarak oturur.
* **Önce Kamera, Sonra Silah Sırası:** Unity FPS oyunlarında standart kural olarak `UpdateCameraPose()` her zaman `ApplyInertialWeaponTransform()` ve süpürme çarpışma kontrolünden **önce** çalıştırılır -> `0` kare kamera gecikmesi.

---

## 2. 3 Aşamalı Adaptif Canlı AI ve Alışkanlık Öğrenme (Elden Ring, Sekiro, Monster Hunter)

### 2.1 Sektör Standartları
* **Hiyerarşik 3 Aşamalı Saldırı FSM (`Windup -> Active -> Recovery`):**
  1. **Windup (Telgraf, `0.45–0.75 s`):** Görsel geriye çekme + sesli uyarı; son `~180 ms`'ye kadar oyuncuya yumuşak dönüş takibi (`tracking`), son `180 ms`'de yön kilitlenir (böylece yan/geri kaçış gerçekten işe yarar).
  2. **Active Hitbox (`0.12–0.18 s`):** Saldırı profiline özel ileri koni/kapsül taraması (`Angle <= HalfConeDeg && Dist <= Reach`). Hedef başına tek isabet (`HitAppliedThisSwing`).
  3. **Recovery (`0.45–0.70 s`):** Açık pencere; oyuncu karşı vuruşla cezalandırabilir.
* **Yönlü Blok ve Alışkanlık Penceresi Öğrenmesi (Habit Window Pattern Learning):**
  * *Sekiro* ve ileri seviye ARPG boss AI'larında canlı, oyuncunun son $N=10$ saldırı yönünü (Üst, Alt, Sol, Sağ, Saplama) bir halka tamponda (`RingBuffer`) tutar.
  * Belirli bir yönden gelen tekrarlı saldırı frekansı $f_d \in [0, 1]$ arttıkça canlının o yönü önceden bloklama veya ters yana kaçınma (dodge) ağırlığı yükselir:
    $$W_{\text{savunma}}(d) = W_{\text{baz}}(d) + \alpha_{\text{halka}} \cdot f_d$$
    Burada $\alpha_{\text{halka}}$ oyuncunun başlangıç merkezinden uzaklığına (`RingDifficultyCalculator`) göre `0.15` (yakın halka) ile `0.85` (dış/zor halka) arasında ölçeklenir.
  * Canlı sağını fiziksel olarak kapattığında solu savunmasız kalır -> oyuncuyu yön değiştirmeye, aldatmaya (`Feint`) veya saplamaya (`Thrust`) teşvik eder.
* **Off-Screen Simülasyon (DES) Sürekliliği (Işınlanma Önleme):**
  * Büyük açık dünya oyunlarında (*STALKER A-Life*, *Kenshi*, *RimWorld/Mount & Blade*) aktif çatışmadaki veya oyuncuya yakın (`< 48 m`) varlıklar asla görüş konisi dışına çıktı diye analitik tabloya indirilmez (histerezis + savaş kilidi). Terfi (promotion) yalnızca hedef chunk tamamen yüklüyken ve görüş kenarı tamponunda yapılır; böylece göz önünde pop-in/ışınlanma sıfırlanır.

---

## 3. Hayatta Kalma Biyolojisi, Sürünme Kurtarma ve Yatak SpawnPoint (Valheim, Vintage Story, Minecraft, Project Zomboid)

* **Downed / BleedOut Döngüsü:** *Project Zomboid* ve modern survival oyunlarında `0 HP` anında ekran kesmek yerine kısa bir kritik kanama/sürünme (`BleedOutCrawl`) penceresi sunulur; oyuncu elindeki bandajla kendini stabilize edebilir veya pes edip doğma ekranına geçebilir.
* **Doğrulanabilir Yatak SpawnPoint (`Minecraft` / `Valheim` Modeli):**
  * Yatak dünyaya yerleştirilen kalıcı bir bloktur. `[E]` ile etkileşime girildiğinde oyuncunun `BedSpawnCoord` (dünya voksel koordinatı) kalıcı kayda yazılır.
  * Yeniden doğma anında motor, `BedSpawnCoord`'da hâlâ bir Yatak bloğu bulunduğunu ve üstünde `2` boş hava vokseli olduğunu doğrular; yatak kırıldıysa veya gömüldüyse oyuncu bilgilendirilerek başlangıç noktasına (`World Origin`) döndürülür.
* **Dengeli Açlık/Susuzluk & Doğrudan Su İçme:** *Vintage Story* / *Green Hell* yaklaşımında 30–40 dk açlık / 20–25 dk susuzluk oyuncuyu boğmadan hayatta kalma gerilimi yaratır; tatlı su kaynağına bakıp `[E]` ile doğrudan içme + matara doldurma erken oyunda tıkanmayı önler.

---

## 4. Su Geçirmez Voksel Mimari Şekilleri ve 24-Yönlü SO(3) Rotasyon (Space Engineers, Vintage Story)

* **Kiral Oktahedral Grup $O$ (24 Pozitif-Determinantlı Rotasyon):**
  * Bir küpün `6 üst yüz × 4 yatay dönüş = 24` doğru rotasyonu vardır ve tamamının determinantı $\det(R) = +1$'dir. Bu nedenle **taban şablon (rot = 0) kesinlikle dışa dönük CCW/CW winding kuralına uyuyorsa**, 24 rotasyonun tamamında winding yönü matematiksel olarak korunur (hiçbir rotasyonda içe dönmez!).
  * **Kritik Kural:** İçerideki hata `Rotate()` matrisinde değil, taban şablonun (`EmitArchVoussoir`, `EmitRoofCorner`) kendi üçgen düğüm sıralamasındadır.
* **Tam Yarı-Birim Kilitlemesi (`±0.50f`) ve `FullFaceMask` Alt-Küme Değişmezi:**
  * *Space Engineers* ve *Vintage Story*'deki altın kural: Bir şekil `FullFaceMask` bitinde belirli bir yüzü "tam kapalı" (`opaque`) ilan ediyorsa, o yüzdeki köşeler **kesinlikle `±0.500f` düzlemine tam oturmak ve o yüzün `%100`'ünü kaplamak zorundadır**.
  * Aksi halde komşu tam küpün yüzeyi gizlenir (face culling) ve `0.49f` gibi 1 cm'lik bir pay bile dünyayı gösteren parlayan bir yarığa (crack/light leak) dönüşür. Tam kapamayan yüzler (örn. `ArrowSlitWall`, `TrapezoidalKeystone`'un dar yüzleri) `FullFaceMask`'te **0** olarak işaretlenmelidir.
* **Taban Eteği (Skirt) Yön Kontrolü:** Araziye gömülme eteği yalnızca bloğun gerçek alt yüzü dünyaca `-Y`'ye bakıyorsa (`rot < 4`) eklenir; duvar/tavan şeklinde çevrilmiş bloklarda etek üretilmez.

---

## 5. Stratocraft İçin Seçilen Mimari Özet

| Alan | Seçilen Endüstri Standardı | Stratocraft Uygulaması |
| :--- | :--- | :--- |
| **Kılıç Kinematiği** | Mordhau Turn Cap + Sabit 240 Hz PD + 2-Kemik Kol IK | `MeleeBiomechanicsSolver` (saf C#), `UpdateCameraPose` önce, ayrık `Thrust` FSM, ayrık normalli kılıç izi |
| **Canlı AI & Savaş** | Elden Ring/Sekiro 3 Aşamalı FSM + Alışkanlık Öğrenme + DES Savaş Kilidi | `CreatureCombatAiProfile` + `CreaturePatternMemory` (son 10 vuruş), 3 aşamalı saldırı + koni hitbox, 2x hasar temizliği, `< 48m` DES kilidi |
| **Ölüm & Yatak** | Zomboid `BleedOutCrawl` + Minecraft/Valheim Doğrulanmış Yatak Spawn | `BedSpawnPointService` + `BedBlock` materyali + 16 HP kilidi temizliği + Ölüm/Doğma UI |
| **Biyoloji & Tüketim** | Vintage Story tempo + Tüm Slotlardan Kullanım + `[E]` Tatlı Su | `Slot 8` kilidi kaldırılır; kompakt Amber/Mavi HUD; matara + `[E]` su içme; bandaj `HeavyBleeding` temizler |
| **Mimari Geometri** | Space Engineers $O(24)$ Su Geçirmez Şablon + Sıkı `FullFaceMask` | `EmitArchVoussoir` / `EmitRoofCorner` / `Ramp` / `Crystal` yeniden yazımı, `±0.50f` snap, şekil `0..14 + 63`, `[R]`/`[Shift+R]` takası |
