# 📋 Stratocraft — Faz 1 & Faz 2 Açık Eksikler, Kök Neden Debug Analizi ve Mimari Çözüm Önerileri (`Devir #047 -> #048`)

> **Durum Notu (`Devir #047`):** Yönlü kılıç/balta/kazma dövüş ve saplanma mekaniği (`Devir #044–#045`), su fiziği, sualtı kamerası, yüzücü dalgaları, 3 kademeli tükenen sıvı kapları (`Devir #046`), Edit Mode / PlayMode arazi dikiş çatlağı onarımı, mağara içi `%100` taban-tavan sütunları ve nadir doğal yüzey mağara girişleri (`Devir #047`) canlı PlayMode testinde **onaylanmıştır**.
> Bu döküman, onay sonrası tespit edilen **4 teknik hata (tam kod satırı kök nedenleriyle)** ve **11 oynanış/dünya/zanaat eksiği** için eksiksiz yol haritası ve çözüm önerilerini içerir.

---

## BÖLÜM A — Tespit Edilen 4 Teknik Hatanın Kod İçi Kök Neden Analizi ve Kesin Çözümleri

### 1. Toprak Islanınca Çamur Rengine Dönüşmemesi (Fizik Çalışıyor Ama Görsel Renk Değişmiyor)
- **Kök Neden (Tam Satır Tespiti):**
  - `SoilMoistureAndMudSystem.StepChunkSoilInfiltration` nemi `0..255` arasında doğru hesaplıyor (oyuncu yavaşlaması bu yüzden çalışıyor).
  - `HybridSurfaceNetsAndShapeMesher.cs` satır `304–306` içinde `dualTemps[dualIdx] = EncodeSoilMoistureTemp(resolvedMat, maxMoisture)` (`1..120`) doğru hesaplanıyor ve `EmitDualQuadAsFacetedTriangles(..., dualTemps, ...)` metoduna geçiriliyor.
  - **Ancak `HybridSurfaceNetsAndShapeMesher.cs` satır `697` içinde:**
    ```csharp
    byte tempA = ResolveMaterialDefaultTemperature(matA), tempB = ResolveMaterialDefaultTemperature(matB);
    ```
    yazıldığı için metoda gelen `dualTemps[i0..i3]` dizisi **hiç okunmuyor** ve `tempA / tempB` her üçgende tekrar `0` ile eziliyor! Dolayısıyla `StratocraftUberShader.hlsl` içindeki `soilWetness01` her zaman `0.0` kalıyor.
- **Kesin Çözüm Önerisi:**
  1. `EmitDualQuadAsFacetedTriangles` (`satır 697`) içinde `dualTemps` dizisindeki köşe nem değerlerinin maksimumu/ortalaması (`Max(dualTemps[i0], dualTemps[i1], dualTemps[i2])`) doğrudan `tempA` ve `tempB` olarak aktarılacak (su geçirmez sınır tamamlaması `satır 669, 676, 683, 690` dahil).
  2. Çimen (`Grass = 3`) ve Toprak (`Dirt = 2`) üzerinde doygunluk arttıkça (`soilWetness01`: `0.0 -> 1.0`) `StratocraftUberShader.hlsl` içinde yeşilden **önce koyu nemli toprağa, doygunlukta (`>= %60`) ise zengin koyu kahverengi balçık/çamur rengine (`sRGB(0.22, 0.13, 0.07)`) + ıslak parlaklığa (`roughness -> 0.12`)** kademeli geçiş yapılacak; kurudukça tekrar doğal çimen/toprak rengine dönecek.

---

### 2. Mağara Koridoruna Zıplarken veya Ağaca Doğru / Geri Geri Yürürken Tepesine Işınlanma ("Asansör Hatası")
- **Kök Neden (Tam Satır Tespiti — `Stage1PlayerMovementBridge.cs`):**
  1. **Tavanı Zemin Sanma (`satır 913`):** `SampleBilinearVoxelGroundHeight` aramaya oyuncunun ayak hizasından değil, **`startY = Floor(refY + 1.35f)`** (göğüs/baş hizası!) noktasından aşağı doğru başlıyor.
  2. **Anında Yukarı Işınlama (`satır 838–849`):** `ResolveVoxelAndColliderDepenetration`, `pos.y < supportY` gördüğünde `burialDepth > 1.35f` ise **tek karede `pos.y = supportY`** yapıyor!
  3. **Koridorda Zıplama Senaryosu:** Oyuncu altındaki bloğa çıkıp alçak mağara koridoruna zıpladığında `0.20s` sonra `_jumpLaunchTimer` bitiyor; o sırada `pos.y + 1.35f` koridorun **üstündeki katı tavan kayasının içine** denk geliyor (`supportY = tavan + 1`). Kod oyuncuyu "zemine gömüldü" sanıp koridor tavanının içine ışınlıyor; bir sonraki karede `pos.y + 1.35f` bir üst kaya katmanını yakalıyor ve **10–15 kare içinde oyuncuyu asansör gibi mağara tavanından yeryüzüne kadar fırlatıyor!**
  4. **Ağaca / Geri Geri Yürüme Senaryosu:** `IsHorizontalStepBlocked` (`satır 860–874`) yalnızca hareket yönünde tek bir noktayı (`next + dir2D * 0.28f`) ve yalnızca `waistY / headY` yüksekliğini kontrol ediyor; oyuncunun kapsül yarıçapını (`±0.32m` 4 köşe) ve diz/ayak hizasını kontrol etmiyor, ayrıca `SampleBilinearVoxelGroundHeight` (`satır 923–934`) `±0.5m` komşu sütundaki alçak yaprak/dal veya eğimli gövde vokselini (`|h - centerH| <= 1.5f` ise) zemin yüksekliğine katıp oyuncuyu ağacın dalına/tepesine çekiyor!
- **Kesin Çözüm Önerisi:**
  1. **Tavan Kilidi:** `SampleSingleColumnGroundY` asla oyuncunun başının üstündeki katı bloğu zemin kabul etmeyecek: aramaya ayak hizasından (`Floor(refY + 0.55f)`) başlanacak ve bulunan katı bloğun **hemen üstünde oyuncunun sığacağı en az 1–2 blok açık hava boşluğu (`!IsHardCollisionVoxel(y+1)`)** şartı aranacak!
  2. **Zıplarken Depenetration Yasağı:** `_verticalVelocity > 0.05f` iken (yukarı zıplama devam ederken) tavan çarpması `pos.y`'yi yukarı ışınlamak yerine `_verticalVelocity = 0f` yapıp oyuncuyu koridorun içine geri düşürecek.
  3. **360° Kapsül Çarpışması (Geri Geri Yürüme Dahil):** `IsHorizontalStepBlocked`, bakış yönünden bağımsız olarak kapsülün 4 köşesini (`±0.30m` X/Z) hem diz (`+0.45m`), hem bel (`+1.05m`), hem baş (`+1.60m`) yüksekliğinde kontrol edecek; böylece ağaca düz veya geri geri yürürken gövde sütununa girmek `%100` imkansız olacak.

---

### 3. Birkaç Saniyede Bir Yaşanan Anlık FPS Düşüşü / Takılma (`80+ FPS -> Anlık Donma`)
- **Kök Neden (Kod İçi Analiz — `Stage1InteractivePlayController.cs`):**
  Oyuncu yürürken her `2.5–4 saniyede` bir `16m` chunk sınırı geçildiğinde **3 ağır işlem tek bir kareye (Main Thread) biniyor**:
  1. **`UpdateNearPlayerColliderRing` (`satır 1804–1833`):** `pCx` veya `pCz` değiştiği karede `mc.sharedMesh = mf.sharedMesh` ve `mc.enabled = true` çağrıları yeni chunk'lar için **PhysX BVH üçgen ağacını Ana İş Parçacığında (Main Thread) senkron pişiriyor (`Physics.BakeMesh` yapılmadığı için tek karede `8–25 ms` kilitlenme)**.
  2. **`UpdateDistantHorizonShellsVisibility` (`satır 2017–2063`):** Her `3` chunk'ta bir (`48m`) `704m` yarıçaplı tüm ufuk halkası (`BuildHorizonClipmapRingMesh`) `Parallel.For` kullanılmadan doğrudan Main Thread üzerinde tek karede baştan sona örülüyor.
  3. **`IsConsolidatedFullPass` Alt-Chunk Bölünmesi (`satır 2411–2455`):** Yeni yüklenen bir chunk'ta tek bir toprak hücresi ıslandığında `entry.IsConsolidatedFullPass` `true -> false` geçerken `10` alt-chunk diliminin tamamı (`subY = 0..9`) Main Thread'de ardışık `10` ayrı mesher çağrısıyla örülüyor (`4` chunk x `10` dilim = tek karede `40` mesher çağrısı!).
- **Kesin Çözüm Önerisi:**
  1. **Arka Plan `Physics.BakeMesh(meshId, false)`:** Yeni chunk mesh'lerinin PhysX çarpışma ağaçları `IJobParallelFor` / arka plan iş parçacığında önceden pişirilecek (`Physics.BakeMesh`), Main Thread'de `mc.sharedMesh` ataması `0.05 ms` sürecek; ayrıca kare başına en fazla `2` collider aktifleştirilecek (Time-Slicing).
  2. **Asenkron / Dilimli Ufuk Halkası:** `BuildHorizonClipmapRingMesh` arka plan görevinde (`Task` / `Parallel.For`) hesaplanıp hazır olduğunda tek `BuildUnityMeshZeroGc` ile yüklenecek.
  3. **Tek-Geçişten Alt-Chunk'a Sıfır-Maliyetli Geçiş:** Zaten `entry.SubChunkTriangles[0..9]` dolu olduğu için `10` dilimi baştan örmek yerine yalnızca kirlenen `dirtyMask` (`1–2` dilim!) örülecek.

---

### 4. Staminanın Sınırsız Gibi Davranması
- **Kök Neden (`Stage1InteractivePlayController.cs` & `PhysicalCharacterControllerSystem`):**
  1. Su kaplarını almak için `[F4]` (Yaratıcı Test Modu) açıldığında `Update()` (`satır 520–526`) her karede `_vitals.Stamina = _vitals.MaxStamina` kilitliyor; `[Tab]` kapatılsa bile `[F4]` açık kaldığında stamina hiç azalmıyor.
  2. Ayrıca `[F4]` kapalıyken bile normal yürüyüş/zıplama aralarında stamina yenilenme hızı (`RegenRate`) yüksek olduğu ve araçla kazma/blok kırma eylemleri stamina düşürmediği için bar neredeyse hep `%100` görünüyor.
- **Kesin Çözüm Önerisi:**
  1. `[Tab]` eşya kataloğu `[F4]`'ten bağımsız hale getirilebilir veya `[F4]` açıkken stamina barının üstünde net **"SONSUZ ENERJİ (F4 AÇIK)"** uyarısı gösterilir; `[ESC]` ayarlarına ayrı "Test Eşya Menüsü Serbest" seçeneği eklenir.
  2. Survival modunda depar (`Sprint`), zıplama (`-6 EP`), tırmanma (`-15 EP`), kaçınma (`-18 EP`), kılıç/balta/kazma savurma ve kazma vuruşları staminayı hissedilir tüketir; eylem bittikten sonra **`0.85 sn` yenilenme gecikmesi (`StaminaRegenDelay`)** eklenir ki oyuncu enerji yönetimini gerçekten hissetsin.

---

## BÖLÜM B — 11 Oynanış, Zanaat, Biyom, Mağara ve Arayüz Eksiği İçin Tasarım & Mimari Önerileri

### 5. Eşyayı Yere Atma (`Item Drop`) Eksikliği
- **Mevcut Durum:** `[X]` / `[Shift+G]` yalnızca elde tutulan silah/aleti kılıftan atmaya çalışıyor; Hotbar'daki blokları, materyalleri, sıvı kaplarını veya `[I]` çantasındaki herhangi bir eşyayı fiziksel 3B dünya nesnesi olarak yere atma yok.
- **Çözüm Önerisi:**
  - **Oyun İçindeyken `[Q]` (veya `[X]`):** Seçili Hotbar slotundaki eşyanın `1` adedini (`Ctrl + Q` ile tüm yığını) oyuncunun baktığı yöne `2.5 m/s` fırlatma hızıyla **3B Fiziksel Dünya Eşyası (`DroppedWorldItem` — `[E]` ile geri toplanabilir)** olarak atar.
  - **`[I]` 3B Kumaş Çanta Açıkken:** Eşyayı fareyle tutup kumaş örtünün **dışına (çimene/yere)** sürükleyip bırakmak veya üzerinde `[Q]` basmak eşyayı doğrudan oyuncunun ayaklarının önüne fiziksel 3B model olarak bırakır (*"%100 Fiziksel Etkileşim"* yasasına tam uyumlu!).

---

### 6. Survival Sıfırdan Başlangıç & Faz 1–2–3 İlkel Zanaat (Ağaç Yumruklamadan Taş Devri Üretimi!)
- **Anayasa Referansı (`GAME LOGIC BIBLE Bölüm 1.3 - Dakika 3–6` & `GELİŞTİRİCİ DOKÜMANI Faz 3 / Faz 7`):**
  - Anayasamızda **"ağacı yumruklayarak odun kırmak" kesinlikle yasaktır**. İleriki fazlarda (`Faz 7`) `16x32x4` Demirci Örsü ve 5 Cihazlı Marangozluk Atölyesi (`Bıçkı Tezgahı`, `Birleştirme Masası`, `Torna`) yer alıyor.
  - Peki **hiçbir aleti olmayan yeni doğmuş bir Survival oyuncusu** ilk baltasını, kazmasını, bıçağını ve su kabını soyut bir Minecraft `3x3` menüsü açmadan, ilerideki Demircilik/Marangozluk sisteminin **"Taş Devri Atası"** olarak nasıl yapacak?
- **Önerilen 3 Aşamalı Fiziksel "Taş Devri (Tier-0)" Başlangıç ve Zanaat Sistemi:**
  1. **Doğadan Elle Toplanabilen Yüzey Kaynakları (`[E]` Tuşu):**
     - Orman ve dere kenarlarında yerde doğal olarak bulunan **Çakmaktaşı / Çay Taşı (`Flint / Loose Stone`)**, ağaç altlarında **Kuru Dal (`Sticks / Branch`)** ve çalılardan/uzun otlardan **Bitki Lifi (`Plant Fiber`)** hiçbir alet gerekmeden `[E]` ile yerden toplanır.
  2. **Aşama 0 — Taş Yontma (`Stone Knapping` — İlerideki `16x32x4` Demirci Örsünün İlkel Atası!):**
     - Oyuncu eline bir **Çakmaktaşı** alır, yerdeki düz bir kayaya (veya elindeki ikinci taşa) bakıp **`[R]` / Sağ Tık** ile 3–4 vuruşta taşı yontarak fiziksel olarak:
       - **Keskin Taş Balta Başı**, **Taş Kazma Başı**, **Taş Bıçak Ağzı** veya **Taş Mızrak Ucu** çıkarır! (Üstelik ilerideki örs mekaniğinin küçültülmüş `4x4` kıvılcımlı yontma hissiyle çalışır!)
  3. **Aşama 1 — Kumaş Örtü / Kütük Üstü Fiziksel Birleştirme (`Ground / Stump Assembly` — İlerideki Marangozluk & Birleştirme Masasının İlkel Atası!):**
     - Soyut "Crafting" penceresi yerine **`[I]` 3B Kumaş Çanta Örtüsünün sağ kenarında "🔨 İLKEL BİRLEŞTİRME ALANI" (veya dünyadaki düz bir kesik kütük üstü)** bulunur:
     - Oyuncu örtünün birleştirme alanına **`1 Kuru Dal + 1 Bitki Lifi + 1 Yontulmuş Taş Başlık`** sürükleyip üzerine `Sol Tık` basılı tutarak (`1.5 sn` lif sarma sesi/animasyonu) bağlar:
       - `Dal + Lif + Taş Balta Başı` -> **İlkel Çakmaktaşı Balta** (Artık ilk ağacı devirebilir ve kütüğü boylayabilir!)
       - `Dal + Lif + Taş Kazma Başı` -> **İlkel Taş Kazma** (Artık Bakır, Kömür, Kireçtaşı ve Taş `IS_ITEM_BLOCK` kazabilir!)
       - `Dal + Lif + Taş Bıçak` -> **Çakmaktaşı Av Bıçağı** (Hayvan derisi yüzme, bitki lifi kesme, hızlı yakın savunma)
       - `2 Kütük / Kalas (Baltayla yarılmış)` -> **İlkel Ahşap Kova / Matara** ve **İlkel Marangoz Kütüğü (`Primitive Sawhorse`)**
       - `5 Ham Taş + 2 Kuru Dal` -> **Kamp Ateşi & İlkel Taş Ocak (`Primitive Kiln/Bloomery`)** -> İlk Bakır eritme ve Kil Matara/Kazan pişirme!

---

### 7. Madde Kristalini Seviye 0'dan Uyandırma ve Seviye Yükseltme (`Tier 0 -> Tier 1 -> Tier 5`)
- **Mevcut Durum:** Kristal `Tier 0` (uykuda/çatlak) başlıyor ama oyun içinde onu `Tier 1`'e uyandıracak veya `Tier 2..5`'e yükseltecek fiziksel oynanış adımı bağlı değil.
- **Anayasa Uyumlu Çözüm Önerisi:**
  - **Tier 0 -> Tier 1 (İlk Uyanış — İlk 10 Dakika):** Doğadaki yüzey mağara girişlerinde veya sığ mağaralarda bulunan **Parlayan Rezonans Kuvarsı (`QuartzGem`)** ilkel taş kazmayla kırılıp (`3 adet Kuvars Parçası`) elde tutulurken `[C]` tuşuna `2 sn` basılı tutularak (veya Kadim Sunak taşında) çatlak kristale yedirilir -> Kristal **Seviye 1 (`Sertlik 1`, `500` kapasite)** olarak uyanır!
  - **Tier 1 -> Tier 2..5 (Rezonans Evrimi):** Anayasa Bölüm `2.2.C` tablosuna birebir sadık kalarak; **Bölge Bosslarından düşen Çekirdekler** (*Lv1 Goblin Kralı / Ork Şefi, Lv2 Troll Kralı, Lv3 Ejderha/Cyclops, Lv4 Titan Golem/Phoenix, Lv5 Kaos Titanı*) VEYA yeraltı zindanlarındaki **Kadim Zindan Sunağı (`DungeonAltarCore`) + Nadir Cevher Katalizörü** (*Bakır/Kuvars -> Ametist/Titanyum -> Mithril -> Astral*) ile rezonansa sokularak kademe kademe yükseltilir.

---

### 8. Madde Kristali İçin "Doğal Görünümlü Dairesel Serpme (`Natural Scatter Placement`)" Modu
- **İhtiyaç:** Şu an kristal blok koyarken tekil küp/şekil koyuyor; oyuncu bir tepeyi, mağara ağzını veya patikayı kapatırken doğadaki gibi organik, dairesel ve pürüzsüz arazi (`ShapeId = 0`, değişen `Density`) koymak istiyor.
- **Çözüm Önerisi:**
  - `[C]` Madde Kristali aktifken **`[V]` (veya `[R]`) tuşuyla 2 İnşa Modu** arasında geçiş yapılır:
    1. **Yapısal / Hassas Mod:** Mevcut tekil hücre yerleştirme.
    2. **Doğal Arazi Serpme Modu (`Natural Terrain Brush`):** Bakılan noktadaki yüzey normaline dik **dairesel bir alanda** (`r = 1.5m .. 3.5m`, kristal seviyesine göre büyür), merkezde yoğun (`Density = 115..127`), kenarlara doğru Perlin/Simplex gürültüsüyle yumuşayan (`Density = 72..98`) ve `ShapeId = 0` olan doğal `SurfaceNets` vokselleri serper. Kristal deposundan tam harcanan yoğunluk kadar kütle düşer; ortaya küp değil, doğadaki tepelerin birebir aynısı organik eğimli zemin çıkar!

---

### 9. Ekranı Kapatan Bilgi Etiketlerinin Taşınması ve `[F3]` Şartına Bağlanması
- **Görsel Tespiti (`media_1794398979401_1c764218.jpg` ve `media_1794398989847_62c90a7c.png`):**
  1. `Stage1VisualHotbarAndInventoryUi.cs:1074` içindeki `"✦ [I] 3B FİZİKSEL KUMAŞ ÇANTA ÖRTÜSÜ..."` şeridi tam çanta slotlarının üstüne binip ilk satırı kapatıyor.
  2. `Stage1InteractivePlayController.cs:2816` içindeki `"Hedef: Çimenli Toprak (Yoğunluk:76 Hasar:0/15) [41m]"` kutusu tam nişangahın (`+`) hemen altında durup oyuncunun önünü kapatıyor.
- **Kesin Çözüm Önerisi:**
  1. Çanta üst bilgi yazısı çanta örtüsünün **en üst kenarının dışına (ekranın üst boşluğuna, ince ve şeffaf tek satır)** taşınacak ve 3 saniye sonra soluklaşacak (veya sadece `[F3]` açıkken detaylı görünecek).
  2. `"Hedef: ... (Yoğunluk:76 Hasar:0/15)"` teknik bilgi kutusu orta ekrandan **tamamen kaldırılacak**; **yalnızca `[F3]` açıkken** sol üstteki `[F3]` debug panelinin içine bir satır olarak yazılacak! Normal oyunda nişangahın çevresi `%100` temiz kalacak.

---

### 10. Mağara Ekolojisi: Mağara Canlıları, Nadir Yeraltı Gölleri, Jeotermal Derinlik Sıcaklığı ve Soğukta Katılaşan Sıcak Lav
- **4 Alt Sistem Tasarımı:**
  1. **Mağara Canlıları:** Mağara galerilerinde karanlığa özgü canlılar (**Dev Mağara Örümceği, Mağara Yarasası/Sürüngen, Kristal Akrep, Derinlikte İskelet Şövalye ve Ateş/Magma Yılanı**) doğacak; tavandan/duvardan ses vererek yaklaşacak.
  2. **Nadir Yeraltı Su Gölleri:** Mağara odalarının yaklaşık **`%12–%18`'inde** (ne her yerde ne de imkansız derecede nadir) çanak tabanlı durgun yeraltı su birikintileri/göletleri (`isSettled = true`) bulunacak.
  3. **Jeotermal Derinlik Sıcaklık Eğrisi (`U-Eğrisi`):**
     - Yüzeyden aşağı ilk inildiğinde güneş kesildiği için mağara **önce soğuyacak** (`Y = 85 -> 55m` arası `-6°C .. +4°C` nemli soğuk taş; meşale/kıyafet önemli!).
     - `Y < 45m`'den çekirdeğe (`Y = 5..25m` manto/magma katmanı) inildikçe jeotermal gradyanla hızla **ısınmaya başlayacak** (`+28°C -> +65°C -> magma yakınında +110°C` bunaltıcı sıcaklık!).
  4. **Lavın Soğuk Bölgelerde Katılaşması & Sıcak Volkanik Kaya Hasarı:**
     - Lav (`FluidType.Lava`) yalnızca çok sıcak bölgelerde (`T_ortam >= 55°C`: Volkan biyomu kalderası veya `Y <= 24m` derin magma katmanı) sonsuza dek akışkan kalır.
     - Serin/normal bölgelere (`T_ortam < 55°C`, yüzey çimenliği veya soğuk mağara) dökülen/akan lav, `8–15 saniye` içinde kenarlardan başlayarak soğuyup **Sıcak Volkanik Kaya (`HotVolcanicRock / MagmaBlock`, `Temp = 220 -> Blackbody kor ışıması`)** bloğuna katılaşır!
     - Bu katılaşmış sıcak volkanik taş etrafındaki **`3.5m` yarıçapa ısı yayar** (soğuk mağarada kamp ısısı olarak bile kullanılabilir!) ama **üzerine basan veya dokunan oyuncuya/canlıya yanık hasarı verir**; dakikalar içinde tamamen soğuduğunda ise normal `Bazalt` taşına dönüşür!

---

### 11. Büyük, Boyutu Rastgele ve Nadirlik Ağırlıklı Zengin Biyom Coğrafyası + Köy / Şehir / Harabeler
- **Mevcut Sorun:** Şu anki biyom geçişleri küçük ölçekli ve bazı biyomlar birbirine çok yakın/tekdüze görünüyor.
- **Önerilen Makro-Voronoi İklim & Nadirlik Mimarisi:**
  - Dünya, sabit kareler yerine **Çarpıtılmış Çok-Ölçekli Voronoi Hücreleri (`Domain-Warped Macro Voronoi`)** ile her biri **`350m` ile `1.400m` arasında rastgele büyüklükte** devasa coğrafi bölgelere ayrılacak:
    - **Çok Sık (`%28`):** Ilıman Yaprak Döken Orman & Çayır Vadileri
    - **Orta-Sık (`%18`):** Yağmur Ormanı / **Amazon Biyomu** (dev gövdeli sık ağaçlar, sarmaşıklar, yoğun nem, turkuaz nehirler)
    - **Orta (`%15`):** **Çöl & Kızıl Kanyon Biyomu** (kumullar, kaktüsler, vaha gölleri, kumtaşı harabeleri)
    - **Orta (`%15`):** **Okyanus, Geniş Sahil Kumsalları ve Takımadalar (Ada Biyomu)** (`SeaLevelY` altında geniş açık deniz, sığ turkuaz kıyılar, kumsal geçiş şeridi ve açık denizde tropik/kayalık adalar)
    - **Nadir (`%9`):** **Kar / Buzul Tayga Biyomu** (kar fırtınalı dağlar, donmuş göller, buz mağaraları)
    - **Nadir (`%8`):** **Uçan Adalar Biyomu (`Sky Archipelago`)** (yerden `60–140m` yukarıda süzülen şelaleli devasa Floatstone adaları, altlarında gölge düşen mistik vadi)
    - **Çok Nadir (`%7`):** **Aktif Volkan & Kül Ovası Biyomu** (merkezde dev lav kalderası, bazalt sütunları, sıcak akışkan lav nehirleri, obsidyen yatakları)
  - **Yerleşimler ve Yapılar:** Biyomlara özgü **Ahşap/Taş Köyler**, yollarla bağlı **Antik Surlu Şehir Kalıntıları**, **Çöl Tapınakları**, **Orman Gözetleme Kuleleri** ve **Yıkık Kemerli Harabeler** düzleştirilmiş doğal temel (`Terrain Flattening Pad`) üzerine yerleştirilecek.

---

### 12. Madenlerin / Cevher Damarlarının Görsel Olarak Ayırt Edici Olması
- **Mevcut Sorun:** Cevherler (`Bakır`, `Demir`, `Kömür`, `Altın`, `Titanyum`, `Mithril`) şu an normal mağara taşıyla aynı düz `SurfaceNets` üçgeni gibi çizildiği için sadece renk tonundan zor seçiliyor.
- **Sıfır-VRAM Görsel Çözüm Önerisi:**
  1. **3B Kristal / Cevher Çıkıntıları (`Ore Nugget Relief`):** Cevher voksellerinin dışa bakan yüzeylerine `HybridSurfaceNetsAndShapeMesher` içinde küçük fasetli **3B cevher yumruları / kristal prizma çıkıntıları** eklenecek; böylece maden damarı daha uzaktan silüetiyle "ben madenim!" diye belli olacak.
  2. **Metalik Damar Parıltısı (`StratocraftUberShader.hlsl`):** Cevher `MaterialId` aralığı için biplanar prosedürel **parlak metalik damar çizgileri (`Ore Vein Specular Glint` + meşale ışığında MatCap yansıması)** açılacak (`0 B` ek VRAM ile Bakır kızıl-turuncu parlar, Demir gümüş-paslı damarlı görünür, Altın/Mithril/Kuvars kristal kıvılcımı saçar).
  3. **Yüzey İzleri (`Outcrops`):** Derindeki büyük bir demir/bakır damarının tam üstündeki yeryüzü kayalarında ufak yüzey cevher izleri oluşacak ki oyuncu nereyi kazacağını jeolojik ipuçlarıyla anlayabilsin.

---

### 13. Canlı Çeşitliliği ve Kılıçla Bloklanabilir Çoklu Saldırı Kalıpları
- **Mevcut Sorun:** Mevcut canlılar sınırlı sayıda ve hep aynı tekdüze ritimde saldırıyor.
- **Çözüm Önerisi:**
  1. **Biyom & Mağara Canlı Kadrosu:**
     - *Uysal / Av:* Geyik (kaçar), Yaban Domuzu (kızdırılırsa düz boynuz şarjı yapar), Kaya Keçisi.
     - *Orman / Yüzey:* Kurt Sürüsü (etraf sarma + sıçrama), Goblin Avcı (hızlı sağ/sol kesik + geri kaçma), Boz Ayı (ağır pençe + dikilip ezme), Ork Savaşçı (balta savurma + kalkan).
     - *Mağara / Volkan / Kar:* Dev Mağara Örümceği (ağ/zehir sıçraması), İskelet Şövalye (kılıç düellocusu — oyuncunun darbelerini bloklayabilir!), Buz Kurdu, Lav Golemi.
  2. **Her Canlıda 3–4 Farklı ve "Okunabilir & Bloklanabilir" Saldırı Yönü (`Telegraphed Directional Attacks`):**
     - Her saldırının **`0.45s – 0.75s` net hazırlık animasyonu (`Wind-up Telegraph`)** ve belirli bir **Saldırı Düzlemi (`Sağ Yatay`, `Sol Yatay`, `Üstten Dikey İndirme`, `Alttan Saplama`)** olacak.
     - Yaratık hep aynı yönden vurmayacak (`Weighted Markov Chain` ile kombo, aldatma/duraksama ve ağır vuruş karıştıracak).
     - Oyuncu `Sağ Tık (RMB) + Fare Yönü` ile kılıcını gelen darbenin düzlemine dik tuttuğunda darbeyi **fiziksel kıvılcımla bloklayabilecek (`Block`)**, son `0.18s` içinde ters savurursa **`MasterDeflect` (Kusursuz Saptırma)** ile yaratığı sersemletebilecek!

---

### 14. Mevsim Döngüsü ve Biyoma Göre Değişen Dinamik Hava Olayları (Yağmur, Kar, Fırtına, Kül)
- **Mevcut Altyapı Avantajı:** Projede `SeasonWeatherSystem` temel zamanlayıcısı ve `SoilMoistureAndMudSystem.AddPrecipitationMoistureToColumn` yağmur emilim kancası **halihazırda hazır!**
- **Tam Entegrasyon Önerisi:**
  1. **4 Mevsim Döngüsü (`İlkbahar -> Yaz -> Sonbahar -> Kış`):** Mevsim geçişleriyle ortam sıcaklığı (`+2°C / +14°C / -2°C / -18°C`), gün ışığı rengi ve ağaç yaprak tonları (Sonbaharda kızıl/altın, Kışın karlı) kademeli değişir.
  2. **Biyoma Duyarlı Hava Olayları (Sıfır-GC GPU/CPU Partikül + Gökyüzü Kararması):**
     - **Orman / Çayır / Amazon:** Yağmur ve Gök Gürültülü Sağanak -> Yağmur yağdıkça açık gökyüzü altındaki topraklar kademeli ıslanıp **çamurlaşır**, çukurlarda ince yağmur birikintileri oluşur, oyuncunun ıslaklığı (`Wetness01`) artar!
     - **Kar Biyomu (veya Kış Mevsiminde Ilıman Biyom):** Kar Yağışı ve Tipi -> Görüş mesafesi kısalır, rüzgar üşütmesi (`Wind Chill`) artar.
     - **Çöl:** Kum Fırtınası.
     - **Volkan:** Sıcak Kül ve Kıvılcım Yağışı.
     - **Mağara / Çatı Altı Kontrolü:** Üstü kapalı mağara veya ev içindeyken yağmur/kar partikülleri kesilir ve oyuncu ıslanmaz/üşümez.

---

## BÖLÜM C — Önerilen Uygulama Sırası (Faz Paketleri)

Bu 15 maddenin birbirini kırmadan, her pakette test edilerek ilerlemesi için **5 ardışık iş paketi** önerilir:

1. ✅ **Paket 1 — Acil Hata & Arayüz Temizliği (`TAMAMLANDI — Devir #048`):**
   - ✅ (#1) Çamur renginin `EmitDualQuadAsFacetedTriangles` + `StratocraftUberShader.hlsl` içinde düzeltilmesi (`yeşil/toprak -> koyu kahve çamur`),
   - ✅ (#2) Mağara koridoru ve ağaçlarda yukarı ışınlanma ("asansör hatası") çözümü (`SampleSingleColumnGroundY` üstü açık hava şartı + `TryResolveCeilingHeadBonk` + 360° 8-noktalı kapsül çarpışması),
   - ✅ (#3) Anlık FPS düşüşü (`MaxRuntimeStreamSpawnPerFrame = 3`, `2.2 ms` `Stopwatch` bütçesi, `Task.Run` Ufuk halkası ve tek-geçiş nem yenilemesi) optimizasyonu,
   - ✅ (#4) Stamina tüketimi (`ConsumeStamina` + `0.90s / 1.65s` `StaminaRegenDelaySeconds`) ve `[F4]` stamina kilidi ayrımı düzeltmesi,
   - ✅ (#5) `[Q]` (`1` adet), `[Ctrl+Q]` (tüm yığın) ve `[I]` çanta örtüsü dışına sürükleyerek 3B eşyayı yere atma (`Item Drop`) + `[E]` ile geri toplama,
   - ✅ (#9) Önü kapatan 2 bilgi penceresinin taşınması / varsayılan kapalı olup sadece `[F3]` açıkken görünmesi.
2. ⏳ **Paket 2 — Taş Devri Survival Başlangıcı, İlkel Craft & Madde Kristali Evrimi (`SIRADAKİ PAKET`):**
   - (#6) Yerde Dal / Çakmaktaşı / Lif toplama + Taş Yontma (`Knapping`) + Kumaş/Kütük üstü fiziksel İlkel Alet üretimi + İlkel Taş Ocak,
   - (#7) Madde Kristali `Tier 0 -> Tier 1..5` uyandırma ve yükseltme mekaniği,
   - (#8) Madde Kristali "Doğal Dairesel Arazi Serpme (`Natural Scatter`)" modu.
3. **Paket 3 — Mağara Ekolojisi, Cevher Görünürlüğü ve Lav Katılaşması:**
   - (#10) Mağara derinlik sıcaklık U-eğrisi (önce soğuk, dipte çok sıcak), soğuk yerde katılaşıp ısı/hasar yayan volkanik lav taşı, nadir yeraltı su gölleri,
   - (#12) 3B çıkıntılı ve metalik damar parıltılı ayırt edici maden/cevher görünümü.
4. **Paket 4 — Büyük Makro-Biyomlar, Köyler/Şehirler/Harabeler ve Mevsimsel Hava Olayları:**
   - (#11) Nadirlik ağırlıklı ve rastgele büyük boyutlu 8+ biyom (Volkan, Kar, Orman, Amazon, Çöl, Okyanus, Sahil, Ada, Uçan Adalar) + Köy/Şehir/Harabe yapıları,
   - (#14) 4 Mevsim döngüsü ve biyoma göre Yağmur (toprağı çamurlaştıran!) / Kar / Kum Fırtınası / Volkanik Kül.
5. **Paket 5 — Zengin Canlı Kadrosu (Yüzey + Mağara) ve Kılıçla Bloklanabilir Çoklu Saldırı AI:**
   - (#13 & #10'un canlı kısmı) Biyomlara ve mağaralara özgü çeşitli canlılar + her canlıda 3–4 farklı yönden okunabilir ve oyuncunun kılıcıyla bloklayıp saptırabileceği (`Block / MasterDeflect`) saldırı kalıpları.
