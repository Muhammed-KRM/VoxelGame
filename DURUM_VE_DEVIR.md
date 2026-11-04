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
| **Devir Numarası** | `#022` (Sağ Tık `RMB` Blokta Kütle Ölçekli Ağırlık/Hız Kısıtı + Kılıcın Ağırlığına Göre Oyuncu Gövdesini ve Kamerayı Savurma/Çekme Eylemsizliği `PendingBodyMomentumLocalVel` + `[F10]` Düellocu NPC & Eğitim Kuklası) |
| **Son Güncelleme Tarihi (ISO-8601)** | `2026-11-04T14:19:00+03:00` |
| **Ortam / Donanım** | `Windows 11`, `NVIDIA GeForce RTX 3050 Ti Laptop GPU 4GB`, `16 GB RAM`, Unity 6 `6000.6.7f1` |
| **Mevcut Çalışma Aşaması** | `Faz 1 & Faz 2 Fiziksel Kılıç Ağırlık/Blok ve Oyuncu Savurma Eylemsizliği (Devir #022) Kodlandı -> Sıradaki: Kullanıcının Play Mode'da Test Etmesi` |
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

### 2.7. P1–P6 Fiziksel Simülasyon Çekirdek Mekanikleri ve Tam Kod Haritası
1. **P1 — 240 Hz Serbest Fiziksel Dövüş & 5-Noktalı Sürekli Namlu Süpürme (`Stage1DirectionalCombatBridge.cs`, `CameraClutchController.cs`, `Stage1CreatureSimulationBridge.cs`):**
   - **3-Bölgeli Yumuşak Kamera Debriyajı (`CameraClutchController`):** $\theta \le 18^\circ$ tam kilit (`0.0x`), $18^\circ < \theta \le 48^\circ$ kuadratik sönümleme, $\theta > 48^\circ$ sınır takibi; kılıç savrulurken kamera topaç gibi dönmez, sadece bilek sınırını aşan fazla açı ve maksimum `%8` kılıç açısal hız torku (`Mathf.Clamp(..., -2.2f, 2.2f)`) gövdeye aktarılır.
   - **5-Noktalı Swept Blade CCD (`BladeSamplePointCount = 5`):** Kabzadan uca ($u \in \{0.2, 0.4, 0.6, 0.8, 1.0\}$) 5 dünya ve kamera-yerel nokta her karede takip edilir (`_prevBladeWorldPoints` -> `_currBladeWorldPoints`). Kamera dönüş hızından arındırılmış saf bilek/kol savurma hızı (`BladeLocalSwingSpeedMetersPerSec >= 1.85 m/s`) ve aktif savurma (`IsActiveAttackSwing`) olmadan kılıç sadece değdi diye hasar vermez.
   - **Dinamik Ağız Hizalaması (`EdgeAlignment01`):** Kılıcın keskin ağzı ile hız vektörü arasındaki açıdan $\cos(\Delta\phi)$ hesaplanır; tam ağız kesiği `1.0x`, yan/ters vuruş (`Flat Hit`) `%30` künt hasar üretir.
   - **Çift Tuş (`LMB + RMB`) İleri Saplama (`_isThrustMode`):** İki fare tuşuna birlikte basılıp fare ileri itildiğinde kesme yerine doğrusal ileri saplama (`CurrentThrustExtensionMeters` uzaması, özellikle `Rapier + Eskrim` stilinde `1.50x` delici hasar) çalışır.
2. **P2 — Fiziksel Envanter, Görünür 3B Kılıflar, Çanta Kanal Transferi & Sertlik Matrisi (`BodyHolsterStateMachine.cs`, `Stage1HolsterAndBackpackController.cs`, `Stage1MatterCrystalAndMiningController.cs`):**
   - **Çıplak El (`Slot 0`) & Kılıfa Sokma (`[0] / [H] / [Q]`):** `RequestSheatheToEmptyHands` ile eldeki silah/alet `0.3s` sürede kılıfa sokulur; kılıftayken karakterin sağ/sol belinde ve sırtında gerçek 3B modelleri (`HolsterVis_HipRight_Sword`, `HolsterVis_HipLeft_Pickaxe`, `HolsterVis_Back_Axe`) görünür.
   - **Hasarla Kesilen `3.0s / 1.5s` Çanta Kanal Transferi (`[I]`):** Kumaş çanta örtüsü açıkken eşya kuşanma `1.5s`, kristale aktarma `3.0s` kanal süresi (`_backpackTransferRemaining`) gerektirir; bu sırada oyuncu hasar alırsa (`NotifyPlayerDamaged`) transfer iptal olur ve çanta kapanır.
   - **Gerçek Malzeme Sertliği (`GetMaterialHardness`) & Alet Çarpanı (`GetToolEfficiencyMultiplier`):** `Mathf.Max(3, ...)` hilesi kaldırıldı; Toprak/Kum `1.0`, Ahşap `2.2` (Balta `3.2x` verimli), Taş `4.5`, Mermer/Bazalt `7.5`, Cevherler `9.5..14.0`, Obsidyen `18.0` (yalnızca Kazma `2.8x` ile kademeli `4-bit DamageState 0..15` biriktirilerek kırılır), `Bedrock (1)` ise hiçbir aletle kırılamaz.
3. **P3 — Canlı 16-Bit Sonlu Akışkanlar & Madde Kristali (`StratocraftFloatingIslandWorldGen.cs`, `FluidCellularAutomataJob.cs`, `Stage1PlayerMovementBridge.cs`, `Stage1MatterCrystalAndMiningController.cs`):**
   - **Aktif Gölet & Lav Kalderası + Stokiyometrik Obsidyen:** Su ve lav çanakları geçirimsiz kaya tabanıyla tutuldu; bent kırıldığında sıvılar akar, `Su + Lav (>= 128)` temas ettiğinde `Doğal Obsidyen (ID=11)`, düşük hacimde (`< 128`) ise buharlaşma oluşur.
   - **Arşimet Yüzme, Boğulma ve Lav Hasarı:** Suya girince kaldırma kuvveti (`Space` ile yukarı yüzme), kafa su altındayken `O2` düşüşü (`LungOxygenPercent`) ve lav içinde `-18 HP/s` yanma hasarı çalışır.
   - **Helisel Bezier Vakum Işını & 4-Renk Stres Önizlemesi:** `[C]` Madde Kristali aktifken sol bilekten hedefe dönen 3B `MatterCrystal_HelicalVacuumStream` ışını çizilir; blok koyarken `EvaluateGhostPlacementPreview` ile `Yeşil (Güvenli)`, `Sarı (Kemer/Konsol)`, `Turuncu (Yüksek Yük)` ve `Kırmızı (Anında Çöker!)` 3B önizleme gösterilir.
4. **P4 — Hilesiz Domino Yıkım & Kütle Korunumlu Pancake Çöküşü (`StructuralCollapseSystem.cs`, `Stage1StructuralCollapseBridge.cs`):**
   - `hasSolidBelow` sahte gökyüzü ankrajı kaldırıldı; altı oyulan kemer, sütun, kule, tapınak ve ağaçlar `Dial's Bucket Geodesic Dijkstra + Single-Pass Tributary Load DAG` ile kopar.
   - Kopan küme tek bir sündürülmüş küp yerine `MinBounds..MaxBounds` içindeki gerçek çok-bloklu voksel geometrisiyle (`localScale = Vector3.one`) düşer, `2B Footprint` altındaki zayıf döşemeleri delerek zincirleme **Pancake Collapse** başlatır ve çarptığı zeminde her bloğu **kendi `MaterialId` kimliğinde `ShapeId = 63` moloz** + **yere fırlayan 3B `DroppedWorldItem`** olarak korur.
5. **P5 — 2-Kemikli Analitik IK & Bölgesel Uzuv Kopması (`Stage1ArticulatedRigAnimator.cs`, `Stage1CreatureSimulationBridge.cs`):**
   - `localScale.y` ile gövde ezmesi kaldırıldı (`bodyTr.localScale = Vector3.one`); Kosinüs Teoremi tabanlı `SolveTwoBoneKneeFlexionDeg` ile diz/kalça bükülür, sürünürken (`Z`) kafa `-64°` karşı açıyla ileri bakar (`RigIK_HeadCounterPitch`) ve sağ omuz kılıç/balta/kazma savurma açısını canlı takip eder.
   - Hem canlılarda hem de oyuncuda (`[F9]`) uzuv koptuğunda eklem kökünde kanlı kesit kapağı (`GoreCap_Severed_*`, `MatID = 245`) açılır ve kopan uzuv `ThrownLimbDebris` olarak parabolik yörüngeyle fırlayıp yerde zıplar.
6. **P6 — Kademeli Kolon Kuşatması, Kaçma Ekolojisi & Phoenix Kor Yumurta (`Stage1CreatureSimulationBridge.cs`):**
   - Yaratıklar yüksekteki oyuncunun altındaki taşıyıcı kolonu tek vuruşta silmek yerine vuruş başına `+5 DamageState` artırarak kademeli kemirir (`ExecuteSupportPillarSiegeStrike`); `DamageState >= 15` olduğunda kolon kırılır ve Domino çöküşü tetiklenir.
   - Canı `%22` altına düşen veya bacağı kopan kara canlıları `CreatureAiState.Flee` durumuna geçip oyuncudan uzağa topallayarak kaçar.
   - Phoenix Boss `0 HP` olduğunda ölmez; `10.0 sn` boyunca `2.500 HP` **Kor Yumurtaya (`CreatureAiState.EmberEgg`)** dönüşür. `10 sn` içinde kırılmazsa `%60 HP` (`1.500 HP`) ve `1.5x` hasar çarpanıyla yeniden doğar.

### 2.8. Kullanıcıyla Soru-Cevapla Netleştirilen Kanonik Tasarım Kararları Özeti
1. **Guard-Konumlu Tam-Yay Kılıç (`Soru 1 -> 1A`):** Sağ Tık (`RMB`) hem Guard konumunu çember üzerinde ayarlar hem de blok (`Parry`) tutar; Sol Tık (`LMB`) + fare savurması kılıcı son Guard köşesinden karşı köşeye tam-yay savurur. Ters köşeden savurmada `150–200 ms` fiziksel geçiş (`Re-Chambering`) uygulanır.
2. **Dövüş Stili Geçişi ve Kazanımı (`Soru 2 -> 2A + 2B Birleşik`):** Oyuncu açtığı Dojo stilleri arasında savaş içinde **`[G]`** tuşuyla anında geçiş yapabilir; yeni stiller dünyadaki Dojo Ustaları/Parşömenlerle açılır ve kullandıkça ustalık (`StanceMastery01`) artar (test sahnesinde tüm stiller açık başlar).
3. **Silah × Stil Uyumsuzluk Cezaları (`Soru 3 -> 3D Tümü`):** Ağır silahı tek el stilde (veya sol kol kopukken) kullanmak: (1) Uç sarkması ve `2.5x` yavaşlama, (2) Savurma sonunda karakteri savuran `Overswing Stagger`, (3) Parry sırasında `Guard Break` ve kılıcın oyuncunun kendi suratına çarpması (`-9 HP`) cezalarının üçünü birden uygular.
4. **Balta/Kazma ve Item Düşmesi (`Ek Soru A -> A1`):** Balta (`[3]`) ve Kazma (`[2]`) zamanlayıcıyla değil, `%100` kılıç gibi fareyi fiziksel savurarak ağaca/kayaya vurulduğunda kırar ve kırılan her blok yere 3B fiziksel item (`DroppedWorldItem`) olarak düşer. Madde Kristali (`[C]`) ise uzaktan vakumlayarak çeker.
5. **Canlı Eti Yarma (`Ek Soru B -> B2 Cleave`):** Kılıç canlıya normal bıçak mesafesinde (`u > 0.22`) değdiğinde durmaz; eti yararak (`Cleave`) içinden geçer, 3B yara izi açar / uzuv koparır ve savurma yayını tamamlar. Yalnızca kabza dibinde (`u <= 0.22`) çarparsa durur.

### 2.9. Devir `#020` — Kullanıcının Kılıç Testi Geri Bildirimleriyle Çözülen 5 Kritik Sorun
1. **Kesim Sonu Ani Kamera Zıplamasının Kaldırılması (`Stage1DirectionalCombatBridge.cs`, `CameraClutchController.cs`):**
   - Savurma bittiği anda (`_slashProgress01 >= 1.0f`) tek karede uygulanan `cameraYawDeg += _slashDirUnit.x * 26.0f;` satırı tamamen kaldırıldı.
   - Savurma biterken elin devam eden fare hareketinin kamerayı aniden fırlatmasını önlemek için `0.11 sn` (`_postSwingClutchGraceTimer`) yumuşak debriyaj geçişi eklendi.
   - Karakterin omuz/gövde dönüşü artık kesim bittikten sonra değil, **kesim devam ederken** çan eğrisi (`Mathf.Sin(s * Mathf.PI)`) boyunca yumuşakça (`~4.5°–6°`) gerçekleşir.
2. **Sağ Tık (`RMB`) ile Kılıcı Aşağı İndirme, Namlunun Aşağı Dönmesi ve `1.38x` Ön-Kurumlu Hızlı Yukarı Kesik:**
   - `_isParryStance` içindeki `if (_guardAnglesRad.magnitude < 0.55f)` iç itme halkası kaldırıldı; `RMB` basılı tutarak fareyi aşağı çektiğinde kılıç kesintisiz en alt Guard konumuna (`y = -1.18`) iner.
   - `ApplyBladeTransformAndSamplePoints` içindeki çelişkili rotasyon blokları birleştirildi: kılıç aşağı indirildiğinde (`pitchNorm < 0`) namlu artık dik (`+Y`) kalmaz, **gerçekten ucu aşağı (`-Y`) bakacak şekilde döner**.
   - `RMB` ile kılıcı aşağı indirip ardından `LMB` ile yukarı kesik atıldığında `0 ms` gecikmeyle anında tam-yay kesik başlar ve **`1.38x` Ön-Kurumlu Hız Bonusu + `1.28x` Hasar Bonusu** (`WasLastSlashPreChambered`) verir.
3. **Havada `Zıpla + Vur` (`IsAerialJumpStrike`) Düzeltmesi (`Stage1CreatureSimulationBridge.cs`, `Stage1DirectionalCombatBridge.cs`):**
   - Oyuncu havadayken (`Space`) savurma yaptığında kılıç açısı otomatik olarak yerdeki hedefe doğru eğilir (`pitchNorm - 0.38f`), dikey kesim menzili aşağı doğru genişletilir ve **`1.40x` Zıpla-Vur (Jump-Strike)** hasar/uzuv koparma bonusu uygulanır.
4. **4 Kılıcın Görsel Olarak Birbirinden Ayırt Edilmesi & `[1]` / `[F8]` Kısayolu:**
   - `RebuildWeaponArchetypeVisualMesh` ile 4 kılıç için farklı 3B prosedürel modeller üretildi: `Longsword` (klasik haç balçak), `Greatsword / Zweihänder` (çift el kabza + namlu üstü `Parierdolche` kancaları), `Rapier` (gümüş/altın çan siperlik `Cup-Hilt` + iğne namlu), `Katana` (yuvarlak altın `Tsuba` + 5 segmentli kavisli `Sori` namlu).
   - Kılıç zaten elindeyken **`[1]` tuşuna tekrar basmak** (veya **`[F8]`** tuşu) 4 kılıç arasında anında geçiş yapar.
5. **4 Dojo Stilinin (`[G]`) Görünür Duruşları ve Alt-Orta HUD Banner (`Stage1InteractivePlayController.cs`):**
   - `Iaido` stilinde kılıç artık FPS kamerasının arkasında kaybolmaz; ekranın sol-alt köşesinde beldeki kında yatay ve net görünür. El boşken **`[G]`** tuşuna basılırsa kılıç otomatik çekilir ve ekranın alt-ortasında büyük **Kılıç & Dojo Stili HUD Banner** gösterilir.

### 2.10. Devir `#021` — Ağırlık Ölçekli Doğal Kılıç Yayları, Sürekli Duvar Depenetrasyonu, Sesli/Görsel Darbe Geri Bildirimi, 4 Dojo Stilinin Özel Yetenekleri ve `[F10]` Düellocu NPC + Eğitim Kuklası (`Stage1DirectionalCombatBridge.cs`, `Stage1CombatFeedbackAndArenaBridge.cs`)
1. **Silah Kütlesine Göre Gerçekçi Savurma Hızı, Post-Swing Recovery ve 3-Fazlı Dairesel Savurma Yayı:**
   - Eskiden savurma süresi silahın gerçek kütlesinden (`0.95kg`..`5.60kg`) bağımsız hesaplanıyor ve savurma biter bitmez `0.00s` bekleme ile saniyede 4-5 kez ağırlıksız sallanabiliyordu.
   - Artık `archetypeMassFactor = Mathf.Pow(wMass / 1.55f, 0.62f)` ile **Rapier (`0.95kg`)** çevik (`~0.22s`), **Katana (`1.35kg`)** akıcı (`~0.28s`), **Longsword (`1.85kg`)** dengeli (`~0.34s`), **Zweihänder (`5.60kg`)** ise oturaklı ve ağır (`~0.58s`) savrulur; her savurma sonunda silah kütlesine göre **`0.07s – 0.36s` Post-Swing Recovery** (`_postSwingRecoveryTimer`) uygulanır.
   - Savurma düz çizgi (`Vector2.Lerp`) yerine **3-Fazlı Dairesel Omuz Yayı** (`Wind-up` -> `Circular Belly Arc` + ileri kol uzanması -> `Follow-Through Overshoot`) izler.
2. **Sürekli 3B Duvar Çarpışması & Geri Sekme (`Continuous Blade-Wall Depenetration`):**
   - Eskiden duvar kontrolü yalnızca `ActiveFullArcSwing` içinde çalışıyor ve açıları eski konuma bıraktığı için duvara yaklaşıp zorlayınca kılıç duvarın içinden geçiyordu.
   - Artık `UpdateAndResolveBladeWorldPoints` **tüm durumlarda** (`IdleInGuard`, `Parry`, `Thrust`, `ReChamberingToStart`, `ActiveFullArcSwing`) 5 namlu noktasını voksel dünyayla kontrol eder; duvara çarpan kılıcı başlangıç köşesine doğru fiziksel olarak geri sektirir (`_slashStartAnglesRad` yönüne bounce) ve duvara sokulmaya çalışıldığında namluyu duvarın dışına iter (`pushAwayAngles` + geri çekilme).
3. **Tam Sesli ve Görsel Vuruş Geri Bildirimi (`Stage1CombatFeedbackAndArenaBridge.cs`):**
   - **Prosedürel PCM Ses Sentezi (Sıfır Dış Asset):** Hafif/ağır kılıç savurma rüzgarı (`WhooshLight` / `WhooshHeavy`), metalik kılıç çarpışması (`SwordClashClang`), taş/duvar sekme çınlaması (`WallRicochetClink`), et kesme (`FleshCleaveHit`) ve ahşap kukla darbesi (`DummyWoodThud`) sesleri çalışma anında üretilip çalınır.
   - **3B Kılıç İz Şeridi (`BladeSwingTrailRibbon`), 3B Kıvılcım/Kan Parçacıkları, `38–55 ms` Hit-Stop ve 3B Yüzen Hasar/Blok Yazıları:** Kılıcın havada çizdiği yay görünür, çarptığı noktadan 3B kıvılcım veya kan fırlar, nişangah ortasında `✕` Hit-Marker yanar ve vuruş noktasının üzerinde **`-68 HP [Foible] (9.4 m/s)`**, **`⚔ KILIÇ ÇARPIŞMASI! (RIPOSTE HAZIR 1.85x!)`** veya **`DUVAR SEKMESİ (Taş)`** yazıları yükselir.
4. **4 Dojo Stilinin (`[G]`) Birbirinden Tamamen Ayrılan Duruşları ve Özel Yetenekleri:**
   - **1) Yüksek Şövalye (`HighGuard_VomTag`):** Sağ-ön dik muhafız duruşu (`(0.24, 0.88)`). **Özel Yetenek — Usta Kontra (`Master Riposte`):** Başarılı blok (`RMB`) veya havada kılıç çarpışması sonrası `1.85 sn` boyunca bir sonraki kesik **`0 ms` gecikmeyle, `1.35x` hızlı ve `1.85x` hasarlı** (`[⚡ KUSURSUZ KONTRA / RIPOSTE 1.85x!]`) çıkar.
   - **2) Çift El Yıkıcı (`TwoHand_Juggernaut`):** Sol-üst baş üstü çapraz Zornhau duruşu (`(-0.84, 0.74)` — Şövalye duruşundan tamamen farklı). **Özel Yetenek — Kasırga Momentum Zinciri & Guard Crush:** Bir kesiğin son `%30`'unda ters yöne `LMB` basılırsa durmadan **Kasırga Zinciri (`Whirlwind Chain x2 / x3`)** bağlanır ve düşmanın bloğunu deler (**`GUARD CRUSH`**).
   - **3) Iaido (`Iaido_QuickDraw`):** Sol beldeki kında yatay duruş (`(-0.98, -0.78)`). **Özel Yetenek — Iaijutsu Şimşek Kesiği:** Kında `0.5s` bekledikten sonra ilk kesik **`0 ms` gecikme, `+24 cm` ileri adım uzanması ve `1.60x` hasarla** çıkar.
   - **4) Eskrim (`Fencing_Punta`):** Merkez-ileri iğne duruşu (`(0.05, 0.08)`). **Özel Yetenek — İğne Hamlesi (`Needle Lunge`) & Otomatik Deflect:** `LMB+RMB` ileri saplamada **`+52 cm` menzil, `2.0x` delici hasar** ve saplama sırasında karşıdan gelen darbeyi otomatik saptırma (**Auto-Deflect**).
5. **Oyuncunun Yanında İki Canlı Test Hedefi (`[F10]` — Düellocu NPC & Mekanik Eğitim Kuklası):**
   - **Mekanik Eğitim Kuklası & Darbe Ölçer (`TrainingAutomaton`):** Oyuncunun sol-önünde durur; yaylı gövdesi darbe aldığında sarsılır, yatay **döner kılıç kolu** ile oyuncunun `RMB` blok ve kılıç çarpıştırma zamanlamasını test eder, üzerindeki 3B tabelada son darbenin **Hasar, Hız (`m/s`), Bölge (`Forte/Mesne/Foible`), Ağız Hizası (`%`) ve DPS** değerlerini canlı gösterir.
   - **Kılıç Ustası Düellocu NPC (`MasterSwordDuelist`):** Oyuncunun sağ-önünde duran eklemli şövalye; elindeki gerçek 5-noktalı 3B kılıçla **4 yönden (`Üstten`, `Soldan`, `Sağdan`, `Alttan`) hazırlanarak (`Wind-up`) saldırır** ve oyuncunun kesiğine karşı yönlü **Guard/Blok** tutar. Oyuncu NPC'nin kılıcına ters vuruşla **Havada Kılıç Çarpıştırma (`Sword Clash`)** veya `RMB` ile **Yönlü Blok (`Parry`)** yapabilir; **`[F10]`** tuşuyla NPC'yi yanına çağırabilir ve modunu (`Aktif Düello` -> `Sadece Blok` -> `Sadece Saldırı`) değiştirebilir.
6. **Sağ Tık (`RMB`) Blokta Kütle Ölçekli Ağırlık & Kılıcın Oyuncuyu Savurma Eylemsizliği (Devir `#022`):**
   - **Sağ Tık (`RMB`) Blok Taşıma Hızı Silah Kütlesine Bağlandı (`guardHandlingFactor`):** Önceden `RMB` basılıyken sabit `0.24f` çarpanı ve `dt * 26f` anlık açı kilitlemesi nedeniyle `5.60 kg` Zweihänder bile `0.95 kg` Rapier kadar hızlı sallanabiliyordu. Artık `RMB` blokta hem fare hassasiyeti hem saniyedeki maksimum açısal hız sınırı (`maxGuardRadPerSec`) hem de PD bilek takip oranı `(1.85 / massKg)^1.15` ile ölçekleniyor; `0.95 kg` Rapier anında bilekten dönerken `5.60 kg` Ağır Kılıç sağ tık blokta da gerçekten ağır, oturaklı ve eylemsizlikli hareket ediyor.
   - **Kılıcın Ağırlığına Göre Oyuncu Gövdesini ve Kamerayı Savurma/Çekme (`PendingBodyMomentumLocalVel` & `ApplyWeaponMomentumHorizontalStep`):** Savurma esnasında oluşan doğrusal/merkezkaç momentum ve omuz dönüşü `(massKg / 1.85)^1.38` ile ölçeklenerek hem kamerayı yumuşakça savurma yönüne çekiyor (`Rapier ~2.9°` vs `Zweihänder ~24°`) hem de `Stage1PlayerMovementBridge.ApplyWeaponMomentumHorizontalStep` üzerinden oyuncu karakterinin ayaklarını/gövdesini voksel çarpışma korumalı olarak savurma yönüne fiziksel olarak çekiyor (`Rapier ~0.27 m/s` neredeyse sabit vs `Zweihänder ~3.1 m/s` güçlü savrulma!).

---

## 3. Yarım Kalan / Kullanıcı Tarafından Henüz Test Edilmemiş Noktalar (Sonraki Oturuma Devredenler)

1. **Kullanıcının Devir `#021` Kılıç Çarpışma, Geri Bildirim ve `[F10]` Düellocu NPC & Eğitim Kuklası Paketini Play Mode'da Test Etmesi (Öncelik #1):**
   - Kullanıcı Play Mode'a girip yanında hazır doğan (veya `[F10]` ile çağrılan) **Kılıç Ustası Düellocu NPC** ve **Mekanik Eğitim Kuklası** üzerinde ağırlık ölçekli kılıç hızlarını, duvar sekmesini, ses/kıvılcım/hasar yazılarını, 4 Dojo stilinin özel yeteneklerini ve kılıç çarpıştırma/blok mekaniklerini test edecek.
2. **İleriki Fazlara (Faz 3+) Bağlanacak Genişletmeler:**
   - **Dojo Stili Açma & Ustalık (`StanceMastery01`):** Şu an test kolaylığı için 4 Dojo stilinin tamamı açık (`AllDojoStancesUnlocked = true`). Faz 3–5 ilerlemesinde dünyadaki **Dojo Ustaları / Parşömenler** ile açılması ve kullandıkça ustalık (`0.0 -> 1.0`) kazanılması bağlanacak.
   - **Örs Üzerinde Dövülen Özel Silah Modelleri:** Şu an `[1]` / `[F8]` ile geçiş yapılan 4 kılıç arketipi (`Longsword`, `Greatsword`, `Rapier`, `Katana`), Faz 3'te `16x32x4` **Çift Boynuzlu Örs** üzerinde dövülen bağımsız 3B GLB modelleriyle genişletilecek.

---

## 4. Güncel Sahne ve Oynanış Tuş Kontrolleri

| Tuş / Eylem | Fonksiyon | Durum |
| :--- | :--- | :--- |
| **`[F10]` Tuşu** | **Kılıç Ustası Düellocu NPC & Eğitim Kuklasını** oyuncunun önüne çağırır ve NPC modunu değiştirir (`Aktif Düello` / `Sadece Blok` / `Sadece Saldırı`) | ✅ Aktif |
| **`RMB` (Sağ Tık) + Fare** | Kılıcın **Guard / Blok (Parry)** yönünü ayarlar (aşağı çekince ucu aşağı döner ve **`1.38x` Ön-Kurumlu Hız Bonusu** hazırlar) | ✅ Aktif |
| **`LMB` (Sol Tık) + Fare Savurması** | Kılıcı/Baltayı/Kazmayı son Guard konumundan hedef yöne **Tam-Yay Dairesel** savurur (`Space` ile havada **`1.40x` Zıpla-Vur**) | ✅ Aktif |
| **`[G]` Tuşu** | **4 Dövüş Sanatı / Dojo Stili** arasında geçiş (`Şövalye: Usta Kontra 1.85x`, `Iaido: Şimşek Kesiği 1.60x`, `Eskrim: İğne Hamlesi 2.0x`, `Juggernaut: Kasırga Zinciri & Guard Crush`) | ✅ Aktif |
| **`[1]` (Tekrar Basış) veya `[F8]`** | **4 Kılıç Arketipi** arasında geçiş (`Longsword 1.85kg`, `Greatsword 5.6kg`, `Rapier 0.95kg`, `Katana 1.35kg` — gerçek kütle ölçekli hız!) | ✅ Aktif |
| **`[F9]` Tuşu** | **Oyuncu Uzuv Kopması Canlı Testi** (`1: Sol Kol Kopar` -> `2: Sağ Bacak Kopar` -> `3: İyileşir`) \| `Shift+F9`: `.sregion` Yükle | ✅ Aktif |
| **`[F7]` Tuşu** | **Rastgele Harita Tohumu (`Seed`) Üretimi** (7 Ağaç + Kule + Tapınak + Kemer yeniden üretilir) | ✅ Aktif |
| **`[1] / [2] / [3] / [4] / [0]`** | `1`: Kılıç (tekrar basınca kılıç değiştirir), `2`: Fiziksel Kazma, `3`: Fiziksel Balta, `4`: İnşa Bloğu, `0/H/Q`: Kılıfa Sok | ✅ Aktif |
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
