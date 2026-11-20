# DOK1 — Dalga A (P0) Hedef ve Gereksinim Dokümanı (Biz Ne Yapmak İstiyoruz?)

> **Doküman Seti Navigasyonu:**
> **[DOK1: Hedef ve Gereksinimler](DOK1_HEDEF_VE_GEREKSINIMLER.md)** | [DOK2: Mevcut Kod Debug & Ekran Görüntüsü Analizi](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md) | [DOK3: Endüstri & Literatür Araştırması](DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md) | [DOK4: Adım Adım Geliştirme Planı](DOK4_ADIM_ADIM_GELISTIRME_PLANI.md)

---

## 1. Amaç ve Kapsam

Bu doküman, Stratocraft'ın **Dalga A (P0 — Kritik Mantık, Fizik, AI, Hayatta Kalma ve Geometri Onarımı + Temel Özellik Genişletmeleri)** fazında çözülmesi gereken tüm hataları, kullanıcı ile hizalanan tasarım kararlarını, canlı PlayTest'te (13 ekran görüntüsü) yakalanan ek görsel/UX hatalarını ve ölçülebilir kabul kriterlerini tanımlar.

### 1.1 Kapsam Sınırı (Dalga A vs. Dalga B.5)
* **Dalga A (Bu Planın Kapsamı):**
  1. Yönlü kılıç biyomekaniği, 1H/2H el-kol-omuz kinematiği, hareket/duruş ivmesi, kontrollü kamera/ayak çekmesi, ayrık saplama (`Thrust`), kılıç izi ve FPS önkol görsel onarımları.
  2. Canlı yapay zekâsı: 3 aşamalı gerçek saldırı (`Windup -> Active Cone Hitbox -> Recovery`), türlere özel çok yönlü saldırılar, yönlü blok/kaçınma, oyuncu alışkanlığı öğrenme, mesafeye göre güç/zeka ölçekleme, Pasif/Nötr/Agresif + sürü/tekil davranışları, **2x hasar hatası** ve **rastgele ışınlanma/kaybolma** onarımı.
  3. Gerçek `0 HP` ölüm akışı (`30 sn BleedOutCrawl` -> Bandaj ile kalkma / `[R]` ile pes etme -> Ölüm Ekranı & Yeniden Doğma) + **Kurulabilir Yatak (`SpawnPoint`)** sistemi + 16 adet ölümsüzlük kilidinin kaldırılması.
  4. Aktif açlık/susuzluk metabolizması, alt HUD barları, **tüm hotbar slotlarından yemek/içmek/bandajlamak**, matara ve tatlı su vokselinden (`[E]`) su içme, bandajın ağır kanamayı (`HeavyBleeding`) temizlemesi.
  5. Mimari şekiller (`0..14 + 63`): su geçirmez dışa dönük winding, `±0.50f` çakışan sınırlar, doğru Türkçe etiketler, şekil `8..14`'ün `[Shift+V]` menüsüne açılması, `[R]` / `[Shift+R]` tuş takası, `[C]` modunda sürekli hayalet önizleme, `ShapeId 63` eşleşmesi.
  6. Ekran görüntülerinde tespit edilen acil P0 HUD/iklim/görsel hataları (`:0.1` sayısal biçim hatası -> `211°C / 371°C`, üst HUD çakışması, siyah kılıç izi, FPS sağda havada asılı kahverengi önkol kutusu).
* **Dalga B.5 (Bir Sonraki Faz — Ayrı Varlık Fabrikası Paketi):**
  * 21 canlı ve 13 boss'un tamamının yüksek detaylı, eklemli Blender MCP + Blockbench 3B model/rig/animasyon varlıklarının `Tools/AutonomousAssetFactory/` hattıyla üretilmesi. *(Dalga A'nın kod ve mantık testlerini şişirmemek için doğrudan Dalga A bitiminde başlatılacaktır.)*

---

## 2. İş Paketleri ve Kilitlenen Tasarım Kuralları

### WP-A1 — Gerçekçi Yönlü Kılıç Biyomekaniği, Saplama ve Görsel Onarım
* **Mevcut Sistemi Çöpe Atmadan Evrimleştirme:** Yönlü kesik, duruşlar (`[G]`), silah arketipleri (`[F8]`), savurma sonrası bekleme (`post-slash hold`) ve `RMB` blok mekaniği korunur; ancak fiziksel gerçekçilik üst seviyeye çıkarılır.
* **Anatomik Omuz–Dirsek–Bilek Kinematiği (1H / 2H):**
  * Kılıç havada serbestçe yörünge çizen bir çubuk gibi değil; insan omzu, dirseği ve bileğinin anatomik eklem sınırları içinde kabzadan tutularak savrulur (ne aşırı yörüngeye girer ne de robotik düz çizgi çizer).
  * Tek el (`1H`) ve çift el (`2H`) tutuş farkı hem savurma tork/hızına hem de karakterin 3B modelindeki kol pozisyonuna yansır.
* **Hareket, Duruş ve Silah Fiziği Entegrasyonu:**
  * Oyuncunun o anki hareketi (**durma, yürüme, koşma, zıplama, eğilme**), seçili dövüş stili (`1H/2H`) ve silahın **şekli, ağırlığı, uzunluğu** savurma hızını, ataletini, başlangıç/bitiş ivmesini ve darbe kinetik enerjisini doğrudan etkiler.
  * Ağır silahlar (`Greatsword` vb.) savurma sırasında oyuncunun ayağını ve kamerayı hafifçe savurma yönüne çekebilir; ancak bu çekiş **sert bir açısal sınır (Turn Cap & Cumulative Pull Clamp)** ile kilitlenir — asla sonsuz dönme, yörüngeye girme veya fare sabitken hayalet kamera kayması (`piecewiseBonusYaw`) oluşmaz.
* **Gerçek Fiziksel Çarpışma:** Kılıç; rakip kılıçlarla (`Blade-vs-Blade`), zemin/taş/ağaç vokselleriyle ve canlılarla fiziksel çarpışır (`Lodge` saplanma / `Ricochet` sekme / `Cleave` yarma).
* **Sabit 240 Hz Yarı-Örtük Euler PD Çözücü:** Savurma yayında 4 keyfi alt-adım + üstüne `Vector2.Lerp(..., 0.65f)` ezmesi kaldırılır; sabit `1/240 s` alt-adım biriktirici ile gerçek tork–atalet integrasyonu yapılır ve canlı fare hareketi yay yönünü yeniden hedeflemez (yalnızca sınırlı drag/accel eğimi verir).
* **Ayrık Balistik Saplama (`Thrust`):** `ScrollUp` veya `LMB+RMB` tek tıklama ile başlar; tek bir `SwingId` üretir, hedef başına 1 kez hasar verir, `~0.35 sn` içinde otomatik geri çekilir (`Retract`). Basılı tutarak her kare `CurrentSwingId++` (testereye dönüşme) tamamen engellenir.
* **Blok (`RMB`):** Mevcut yönlü blok korunur; animasyon temizlenir, blok kaldırma/yön değiştirme hızı silah kütlesiyle ölçeklenir ve kılıç izi siyah üçgeni ile FPS sağda yüzen önkol kutusu düzeltilir.

### WP-A2 — Gerçekçi & Adaptif Canlı AI, 3 Aşamalı Saldırı, Işınlanma ve 2x Hasar Onarımı
* **2x Çift Hasar Düzeltmesi:** `TakeDamageRegional` zaten HP'yi düşürdüğü için `Stage1CreatureSimulationBridge` ve `Stage1CombatFeedbackAndArenaBridge` içindeki ikinci `CurrentHp -= actualDmg` satırları kaldırılır. Ekranda yazan `-X HP` ile oyuncunun can barındaki gerçek düşüş birebir eşit olur (`ActualHpDrop == ReportedActualDamage`).
* **3 Aşamalı Gerçek Fiziksel Saldırı:**
  1. **Windup / Telegraph (`0.45–0.75 sn`):** Canlı saldırı yönüne göre kolunu/kafasını/silahını belirgin biçimde geriye çeker (görsel + sesli telgraf). Bu evrede hasar yoktur.
  2. **Active Hitbox (`0.12–0.18 sn`):** İleri doğru fiziksel koni/kapsül menzil kontrolü yapılır. Oyuncu yan/geri kaçtıysa darbe **ıskalar (`Whiff`)**; doğru zamanlanmış `RMB` bloğu darbeyi **%100 engeller ve Riposte açar**.
  3. **Recovery (`0.45–0.70 sn`):** Canlı toparlanır ve karşı saldırıya açık kalır.
* **Türlere Özel Çoklu Saldırı & Güç:** Her canlı arketipinin kendi saldırı repertuarı (yukarıdan aşağı, aşağıdan yukarı, sağdan, soldan, çift yan pençe/boynuz/gaga vb.) ve güç/hız profili bulunur.
* **Canlı Yönlü Blok, Kaçınma ve Öğrenme (Adaptation):**
  * Zeki/silahlı ve yüksek kademe canlılar oyuncunun saldırı yönünü okuyup **fiziksel yönlü blok** (sağı kapatırsa solu açık kalır) veya **geri/yan kaçınma (dodge)** yapabilir.
  * Canlılar kısa süreli alışkanlık tamponu (son 8–10 oyuncu saldırısı) tutar: oyuncu sürekli aynı yönden vurursa canlının o yönü savunma/karşı hamle ağırlığı artar.
  * Canlı gücü, tepki süresi ve öğrenme katsayısı başlangıç noktasından uzaklaştıkça (`RingDifficultyCalculator`) ölçeklenir.
* **Mizaç ve Ekoloji (Game Logic Bible Uyumlu):**
  * **Pasif:** Saldırmadıkça kaçınır/otlar/çevreyle etkileşir.
  * **Nötr:** Kışkırtılmadıkça saldırmaz; vurulunca veya yuvasına/sürüsüne girilince karşılık verir.
  * **Agresif:** Görüş/algı menzilinde avlanır; **sürü (pack)** türleri yan yana kuşatma (flank) ve sürü uyarısı yapar, **tekil (solo)** avcılar pusu/birebir baskı uygular.
* **Rastgele Işınlanma ve Kaybolma Onarımı:**
  * Savaş/takip (`AggroChase`, `MeleeAttack`, `Flee`, `Windup`, `Active`) halindeki ve oyuncuya yakın (`< 48m`) canlılar asla DES Off-Screen'e indirilip yok edilmez.
  * DES terfi (`PromoteFromDes`) ve zemin yükseklik örneklemesi (`SampleStandableHeight`) yalnızca chunk yüklüyken, dikey sapma sınırlanarak ve görsel pop-in olmadan yapılır; tek karede `> 1.75m` izinsiz sıçrama tamamen engellenir.

### WP-A3 — Gerçek Ölüm Akışı (`0 HP` -> `BleedOutCrawl`) ve Kurulabilir Yatak (`SpawnPoint`)
* **16 Ölümsüzlük Kilidinin Kaldırılması:** Hayatta Kalma modunda (`[F4]` kapalı) `CurrentHp` artık `20` veya `1`'de kilitlenmez; `0.0f` değerine inebilir. *(Yalnızca Eğitim Düellocu NPC saldırıları eğitim amaçlı `1 HP`'de durmaya devam eder.)*
* **30 Saniyelik Sürünme (`BleedOutCrawl`):**
  * `CurrentHp <= 0` olduğunda karakter `CharacterStance.BleedOutCrawl` duruşuna geçer (`BleedOutTimerSeconds = 30.0f` geri sayar).
  * Oyuncu bu 30 saniye içinde envanterindeki **Tıbbi Bandajı (`MedicalBandage`)** kullanarak kanamayı durdurup düşük canla (`25 HP`) ayağa kalkabilir veya **`[R]` (Pes Et)** tuşuna basarak doğrudan ölüm ekranına geçebilir.
* **Ölüm Ekranı ve Yeniden Doğma:**
  * Süre dolduğunda (`IsDead == true`) ölüm ekranı açılır (ölüm sebebi + "Yatakta Doğ" / "Başlangıçta Doğ" butonu).
* **Kurulabilir Yatak (`Bed / SpawnPoint`):**
  * Oyuncu dünyaya **Yatak** koyabilir ve **`[E]`** ile etkileşime geçip **Yeniden Doğma Noktası (`SpawnPoint`)** olarak kaydedebilir.
  * Doğma anında yatak bloğunun hâlâ yerinde ve üstünün engelsiz olduğu doğrulanır; yatak kırıldıysa veya yok olduysa sistem oyuncuya bildirim verip güvenli başlangıç noktasına (`World Origin`) doğurur.

### WP-A4 — Aktif Açlık, Susuzluk, Hidrasyon, Tüm Slotlardan Tüketim ve HUD
* **Dengeli Hayatta Kalma Hızı:** Zorla `HungerKcal = 2000f` / `ThirstHydration = 100f` sıfırlaması kaldırılır. Normal tempo: **Açlık ~30–40 dk** (`2500 -> 0 kcal`), **Susuzluk ~20–25 dk** (`100% -> 0%`); koşma/dövüş/sıcak hava tüketimi makul oranda artırır. `[F4]` Yaratıcı Modda tüm vitaller `%100` kilitli kalır.
* **Her Hotbar Slotundan Tüketim (Ekran Görüntüsü #13 Kritik Onarımı):**
  * `ActiveHolsterSlot == 8` zorunluluğu kaldırılır! Oyuncu hotbar'da **hangi slotta (0..9)** olursa olsun elinde `FoodCookedMeat`, `FoodWildBerries`, `FoodCleanWater`, `PotionHealth`, `PotionStamina`, `MedicalBandage` veya su dolu `ToolFluidFlask` tutuyorsa sol/sağ tık ile doğrudan tüketebilir.
* **Gerçek Beslenme, Hidrasyon ve Bandaj Etkileri:**
  * `FoodCookedMeat`: `+35 HP`, `+40 EP`, **`+650 kcal`**, `+0.8°C`.
  * `FoodWildBerries`: `+12 HP`, `+20 EP`, **`+180 kcal`**, **`+8%` su**.
  * `FoodCleanWater` / Su Dolu Matara (`ToolFluidFlask`): **`+35%` su**, `+20 EP`, `-0.5°C` serinleme (mataradan 1 yudum hacim eksiltir).
  * **Doğal Tatlı Su Vokselinden İçme (`[E]`):** Oyuncu nehir/göl tatlı su (`FluidType.Water`) kenarına bakıp `[E]` bastığında elinde matara yoksa doğrudan su içebilir (`+25%` susuzluk, kısa cooldown).
  * **`MedicalBandage`:** `+25 HP` + **`BiologicalAfflictionFlags.HeavyBleeding` bayrağını kesin olarak temizler** (ve `BleedOutCrawl` durumundaysa oyuncuyu ayağa kaldırır).
* **Kompakt Alt HUD Barları:** Mevcut kırmızı `CAN` ve yeşil `ENERJİ` barlarının hemen yanında/üstünde okunaklı **Amber/Turuncu Açlık (`TOK`)** ve **Mavi Susuzluk (`SU`)** barları gösterilir; `%20` altında nabız/uyarı rengi verir.

### WP-A5 — Mimari Şekiller (`0..14 + 63`), Su Geçirmez Winding, Rotasyon ve Hayalet
* **Tüm Şekillerde Su Geçirmez Dışa Dönük Winding (`InvertedNormalCount == 0`, `OpenBoundaryEdgeCount == 0`):**
  * `EmitArchVoussoir` (`ShapeId 4, 18, 55..58` — tapınak kemerleri ve ağaç dalları): 14/16 ters üçgen ve kendi kendini kesen konkav katlama tamamen düzeltilir; kapalı, konveks dilimli, dışa dönük normalle örülür.
  * `EmitRoofCorner` (`ShapeId 13, 39..42`): ters `+Z` yüzeyi düzeltilir; `±0.49f` yerine tam `±0.50f` sınır kullanılır.
  * `EmitSlopedRamp` (`ShapeId 3, 10, 11`), `HalfSlab (8)`, `StairStep (14)`, `TrapezoidalKeystone (5)`, `ArrowSlitWall (15)`, `ButtressSupport (16)`: `FullFaceMask` ile kapattığı iddia edilen tüm yüzler tam `[-0.50, +0.50]` kenarına oturtulur veya tam kapamayan yüzler `FullFaceLut`'tan çıkarılır (sıfır 2 cm / 22 cm duvar yarığı!).
  * `EmitFacetedCrystalCluster` (`ShapeId 7`): 24-yönlü rotasyon uygulanır ve alt kapak (bottom cap) eklenir.
  * **Döndürülmüş Etek Sızması (`skirtDrop`):** `ExtendFoundationSkirt` yalnızca tabanı `-Y`'ye bakan (`rot < 4`) bloklarda uygulanır; yan/ters dönmüş bloklarda dışarı taşmaz.
* **Kullanıcı Arayüzü & Kontroller:**
  * `GetShapeDisplayName` (`0..14 + 63`) etiketleri `HybridSurfaceNetsAndShapeMesher` ile %100 birebir eşleştirilir.
  * Zaten motor içinde var olan **Şekil `8..14`** (`Yarım Döşeme`, `Çeyrek Kiriş`, `22.5° Alt Rampa`, `22.5° Üst Rampa`, `45° İç Çatı Köşesi`, `45° Dış Çatı Köşesi`, `Merdiven Basamağı`) `[Shift+V]` döngüsüne eklenir.
  * Tuş takası: **`[R]` = `90°` Yatay Yaw (0..3)**, **`[Shift+R]` = 24-Yönlü 3B SO(3) (0..23)**.
  * **`[C]` Mimari Modda Sürekli Hayalet Önizleme:** Sağ tıka basılı tutmadan da hedef blokta yarı saydam hayalet şekil + gerçek yönelimi sürekli görünür.
  * **`ShapeId 63` Eşleşmesi:** Hayalet ve dünyaya konulan blok aynı 64-alt-voksel mikro-havuz/geometri yolunu kullanır; moloz parçasına bozulma biter.

### WP-A6 — Ekran Görüntüsü Kaynaklı Acil P0 Görsel & HUD Onarımları
* **Siyah Kılıç İzi (`UpdateBladeSwingTrailMesh`):** Ön ve arka yüzler paylaşımlı vertex yerine ayrık normal (veya çift-yüzlü unlit/additive şerit) ile örülür; `+N + (-N) = 0` siyah üçgen tamamen yok olur.
* **FPS Sağda Yüzen Kahverengi Önkol Kutusu:** `Stage1ArticulatedRigAnimator` içinde üst kol gizliyken sağ/sol önkolun FPS boş elde havada bağımsız kutu olarak görünmesi düzeltilir; FPS çıplak el yalnızca `StepFirstPersonBareFistVisuals` (veya omuzdan bağlı doğru 1H/2H el rigi) üzerinden temiz çizilir.
* **C# `:0.1` Biçim Hatası (`211°C / 371°C / 10131 hPa`):** `Stage1InteractivePlayController.cs` içindeki tüm `:0.1` biçimleri `:0.0` (1 ondalık basamak) olarak düzeltilir ve üst orta/üst sağ HUD bantlarının birbirinin üstüne binmesi önlenir.
* **Biyom Sıcaklığı / Yağış Fazı Tutarsızlığı:** `+44°C` Kızıl Kumtaşı Kanyonu'nda `Hava: 21.1°C` sabitlenmesi ve `Faz: Snow` görünmesinin nedeni giderilir (gerçek biyom baz sıcaklığı ile yağış fazı eşlenir).

---

## 3. Ölçülebilir Kabul Kriterleri (Acceptance Criteria)

| Kod | Kriter | Hedef Metrik (`WaveADeepDiagnosticProbe` / Birim Test) |
| :--- | :--- | :--- |
| **AC-A1** | PD çözücü ezmesi ve canlı yön saptırma | `PdOverwriteEvents == 0`, `LiveRetargetEvents == 0`, `MaxPiecewiseDrift == 0.000 deg/f` |
| **AC-A2** | Sabit 240 Hz alt-adım ve 1 kare kamera gecikmesi | `SubstepDt == 1/240s`, FPS `MaxCamLagPos < 0.002m`, `MaxCamLagAng < 0.05 deg` |
| **AC-A3** | Saplama (`Thrust`) tek vuruş | Tek `Thrust` döngüsünde `SwingIdIncs == 1`, hedef başına `<= 1` vuruş |
| **AC-A4** | Ağız hizalaması dt bağımsızlığı | `MaxEdgeRollDelta <= 18.0 * dt` (yüksek FPS'te anında Flat-Slap yok) |
| **AC-A5** | Kılıç izi ve FPS önkol | Kılıç izinde sıfır-normal üçgen `0`, FPS'te havada kopuk önkol kutusu `0` |
| **AC-A6** | Canlı 2x hasar sonu | Tüm canlı ve Düellocu vuruşlarında `DoubleDamageHits == 0` (`|ActualHpDrop - ReportedActualDamage| < 0.05`) |
| **AC-A7** | 3 aşamalı canlı saldırısı & ıskalama | `Windup (0.45–0.75s) -> Active Cone -> Recovery`; menzilden çıkınca `0` hasar, `RMB` blokta `0` hasar + Riposte |
| **AC-A8** | Canlı ışınlanma/kaybolma sonu | `CreatureTeleports == 0` (savaşta/yakında `TeleportJumpMeters <= 1.75m`) |
| **AC-A9** | Canlı yönlü blok, kaçınma & öğrenme | Tekrarlı aynı yön saldırılarında canlının o yön blok/kaçınma olasılığı ölçülebilir şekilde artar; uzak halkada daha güçlü/zeki |
| **AC-A10** | Gerçek ölüm & Yatak SpawnPoint | Survival'da `HpFloorClamps == 0`, `0 HP -> 30s BleedOutCrawl -> Bandaj veya Ölüm Ekranı -> Yatakta (kırıldıysa Origin'de) doğma` |
| **AC-A11** | Açlık/Susuzluk & tüm slot tüketimi | `HungerForcedResets == 0`, `ThirstForcedResets == 0`; Slot `0..9` herhangi birinden et/meyve/su/bandaj/iksir çalışır; Bandaj `HeavyBleeding` temizler |
| **AC-A12** | Mimari şekiller (`0..14 + 63`) kusursuzluk | `LabelMismatches == 0`, `InvertedShapes == 0`, `GapOrOpenEdgeShapes == 0`, `SkirtOutOfBoundsLeak == false` |
| **AC-A13** | HUD biçim & performans bütçesi | `0.1` literal hatası `0`; `0 Error / 0 Warning` (`.NET 10` + Unity 6); `0 B/frame GC` (probe kapalı); `< 16 MB VRAM` |
