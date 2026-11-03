# Play Mode Canlı Test Hataları — Kök Neden (Root-Cause) ve Log Analiz Raporu

**Tarih:** 2026-11-03  
**Kapsam:** Play Mode testinde gözlemlenen 5 ana çalışma zamanı (runtime) hatasının ve Unity Console loglarının (`console_get`) kod satırı bazında kök neden analizi ve çözüm planı.

---

## 1. Sıvıların (Su ve Lav) Teste Girer Girmez Bir Yere Akıp Yok Olması

### Gözlemlenen Sorun
Play Mode başlar başlamaz Güneydoğu'daki su havuzu ve lav kalderası bir yöne doğru hızla akıp birkaç saniye içinde tamamen kayboluyor.

### Kök Neden (3 Birleşik Hata)
1. **Havuz ve Kaldera Tabanının (`y = 6`) Geçirgen (Porous) Üretilmesi (`Density < 96`):**
   - `Assets/_Stratocraft/Physics/Data/FluidPorosityUtility.cs` (`ComputeCellFluidCapacity`, satır 43–61) içinde bir SDF vokselin sıvıyı tamamen tutabilmesi (geçirimsiz kaya olması) için yoğunluğunun `Density >= 96` (`SdfConfinedSolidLevel`) olması şarttır. `64 <= Density < 96` olan tüm hücreler **gözenekli kıyı** kabul edilerek içine sıvı alır (`1..96` kapasite); `Density < 64` olan hücreler ise **tam açık hava (`255` kapasite)** kabul edilir.
   - Ancak `Assets/_Stratocraft/WorldGen/StratocraftFloatingIslandWorldGen.cs` (satır 116–166) içinde su havuzu (`inWaterPool`) ve lav kalderasında (`inLavaCaldera`) yüzey yüksekliği `topH = 6.85f` olarak sabitlenmiştir ve su/lav `y = 7` ile `y = 8` katmanlarına `isSettled: false` (aktif) olarak yerleştirilmiştir.
   - Suyun/lavın hemen altındaki taban katmanı olan **`y = 6`**'da:
     $$\text{distFromTop} = 6.85 - 6.0 = 0.85 \implies \text{densityInt} = \text{Round}(64 + 0.85 \times 28) = 88 < 96$$
     Yani havuzun ve kalderanın **tüm tabanı (`y = 6`) geçirgendir (`Density = 88 < 96`)**!
2. **Kaldera Altında Yüzen Adanın Alt Gövdesinin (`bottomH`) Yükselerek Tabanı Delmesi (`y = 5` Açık Hava):**
   - `StratocraftFloatingIslandWorldGen.cs` (`SampleIslandUnderbellyY`, satır 69–79) fonksiyonunda adanın alt bazalt karnı (`bottomH`), kıyıya yaklaştıkça `normR * normR * 4.45f + crag` formülüyle **`y = 5.5m .. 6.2m`** seviyesine kadar yükselmektedir.
   - Güneydoğu lav kalderasının (`wx = 9..11, wz = -10..-7`) tam altında `bottomH ≈ 5.5m` olduğu için `y = 5` katmanında `distFromBottom = 5 - 5.5 = -0.5` olmakta ve `densityInt = 50 < 64` (**tam açık hava kapasitesi = 255**) oluşmaktadır!
3. **U-Borusu / Sifon BFS'nin (`UpdateHydrostaticHeadAndSolveUPipes`) Yüzey Suyunu Taban Altına Vakumlaması ve Buharlaştırması:**
   - `Assets/_Stratocraft/Physics/Jobs/FluidCellularAutomataJob.cs` (satır 447–589) içindeki hidrostatik basınç çözücüsü, `y = 8`'deki su/lav kaynağını (`bestSourceY = 8`) alıp `y = 6` ve `y = 5`'teki geçirgen/delik taban hücrelerine (`bestOutletY = 5..6`) pompalar.
   - Tabandan (`y = 6 -> 5`) adanın altındaki boşluğa (`y = 4 -> 3 -> 2 -> 1 -> 0`) akan su ve lav, chunk'ın en alt katmanı olan `y = 0`'da (burada hiç katı voksel üretilmediği için `voxels[y=0] = Air`) `16x16` boyunca ince bir tabaka halinde yayılır.
   - İnce tabaka halinde yayılan düşük kütleli (`Mass < 128`) su ve lav hücreleri birbirine değdiği anda `TryReactWaterAndLava` (satır 36–77) kuralı gereği (`V_su < 128 || V_lav < 128 -> Buharlaşma, her iki sıvı = 0`) birbirini **buharlaştırarak sıfırlar**, kalan `< 8` kütleli hücreler ise `FluidSurfaceSkirtMesher` eşiğinin (`Mass >= 8`) altına düştüğü için tamamen görünmez olur!

---

## 2. Kılıç Sistemi Hataları (Topaç Gibi Dönme, Dik Tutma ve Vurmadan Öldürme)

### 2.A. Karakterin Kılıçla Birlikte Topaç Gibi Sonsuz Dönmesi
- **Sorunlu Dosya ve Satır:** `Assets/_Stratocraft/Character/Camera/CameraClutchController.cs` (satır 88–95) ve `Assets/_Stratocraft/Character/Stage1DirectionalCombatBridge.cs` (satır 153–171).
- **Kök Neden:**
  ```csharp
  WristYawRadians = Math.Clamp(WristYawRadians + smoothDeltaYaw, -1.35f, 1.35f);
  WristPitchRadians = Math.Clamp(WristPitchRadians + smoothDeltaPitch, -1.10f, 1.10f);

  CameraYawRadians += EvaluatePiecewiseClutchDelta(WristYawRadians) * deltaTime * 8.0f;
  ```
  `WristYawRadians`, fare hızı (delta) değil, bileğin ekran merkezinden **kümülatif açısal sapma konumudur** (`±1.35 rad`). Sol veya sağ tık basılı tutulduğu sürece (`isClutchEngaged == true`), `WristYawRadians` sıfıra geri dönmez.  
  Ancak **91. satır**, bu sabit açısal konumu her karede `deltaTime * 8.0f` ile çarparak **açısal hız (angular velocity)** gibi `CameraYawRadians` üzerine eklemektedir!  
  Sonuç olarak oyuncu sol tıka basılı tutup fareyi `20°` (`0.35 rad`) sağa çekip **fareyi tamamen durdursa bile**, `WristYawRadians = 1.0 rad` kaldığı için kamera ve karakter her saniye **`~412°/sn` (`7.2 rad/sn`) hızla kendi etrafında topaç gibi dönmeye devam eder!**

### 2.B. Kılıcın Gerçekçi Savrulmak Yerine Dik (Şamdan Gibi) Tutulup Sallanması
- **Sorunlu Dosya ve Satır:** `Assets/_Stratocraft/Character/Stage1DirectionalCombatBridge.cs` (`ApplyInertialWeaponTransform`, satır 300–330).
- **Kök Neden:**
  ```csharp
  float bladePitchDeg = -_currentBladeAnglesRad.y * 58f + (_isThrustMode ? 42f : 0f);
  float bladeYawDeg = _currentBladeAnglesRad.x * 62f;
  float edgeRollDeg = -_currentBladeAnglesRad.x * 34f + Mathf.Sin(_edgeRollRad) * 22f;
  swingRot = Quaternion.Euler(bladePitchDeg, bladeYawDeg, edgeRollDeg);
  ...
  Quaternion fpsBaseRot = Quaternion.Euler(14f, -12f, 8f);
  weaponPivot.rotation = playerCamera.transform.rotation * fpsBaseRot * swingRot;
  ```
  Kılıç modeli (`weapon_hilt_ironwood_longsword.glb` + `Forged16x32x4_DamascusSteelBlade`) yerel **`+Y` (yukarı)** ekseni boyunca dik üretilmiştir.  
  `fpsBaseRot = Euler(14°, -12°, 8°)` kılıcı neredeyse dimdik (`+Y`) tutmaktadır. Oyuncu yatay savurma (`_currentBladeAnglesRad.x`) yaptığında kılıç ucu yatay kesme düzlemine (`Z` ekseninde `~85°–90°` yatırılarak) yatırılmak yerine sadece kendi dikey sapı etrafında (`bladeYawDeg`) burkulmakta ve en fazla `34°` eğilmektedir. Bu yüzden kılıç karşıya doğru kesme arkı çizmek yerine elde dik bir sopa/meşale gibi sağa sola sallanmaktadır.

### 2.C. Önümüzdeki Düşmanların Kılıcı Savurmadan (Kendiliğinden) Ölmesi
- **Sorunlu Dosya ve Satır:** `Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs` (satır 216–219) ve `Assets/_Stratocraft/Character/Stage1CreatureSimulationBridge.cs` (`EvaluateSweptBladeCombat` & `TryFindBestBladeSegmentHit`, satır 332–389 ve 438–463).
- **Kök Neden (3 Birleşik Hata):**
  1. **Saldırı Tuşuna Basılmasa Bile Her Karede Çalışması:** `Stage1InteractivePlayController.cs:218` içinde `canCombat` (Slot 1, 2 veya 3 seçiliyse) doğru olduğu sürece, **oyuncu sol tıka hiç basmasa ve kılıcı hiç savurmasa bile** her `Update` karesinde `_creatureBridge.EvaluateSweptBladeCombat(...)` çağrılmaktadır!
  2. **Kılıç Hızının Oyuncu Yürüme Hızıyla (`World-Space`) Hesaplanması:** `Stage1CreatureSimulationBridge.cs:445` satırında:
     ```csharp
     float speed = (currPts[k] - prevPts[k]).magnitude / dt;
     ```
     `currPts[k]` dünya (world-space) koordinatlarıdır. Oyuncu kılıcı hiç sallamadan sadece `W/A/S/D` ile yürüdüğünde (`4.5 – 7.0 m/s`) veya kamerayı çevirdiğinde bile kılıç noktalarının dünya hızı `> 2.0 m/s` eşiğini (satır 375) aşmaktadır!
  3. **Vuruş Bekleme Süresi (`_hitCooldownTimer`) Kontrolünün Unutulması ve Devasa Çarpışma Küreleri:** `Stage1DirectionalCombatBridge.NotifyBladeHitRegistered()` içinde `_hitCooldownTimer = 0.18f` set edilmesine rağmen, `EvaluateSweptBladeCombat` bu sayacı **hiç kontrol etmemektedir**! Üstelik `TryFindBestBladeSegmentHit` içindeki küre yarıçapları (`0.62m`, `0.65m`, `0.75m`) çok büyüktür. Ash Stalker yaratıkları oyuncuya doğru koşup `2m` yakına geldikleri anda, oyuncunun elinde sabit duran kılıca değerek **her karede (saniyede 60 kez) 25–90 HP hasar alıp `0.1 saniye` içinde kendiliğinden ölmektedir!**

---

## 3. Yapısal Çökme (Domino) ve Moloz (`LooseRubble`) Görünürlüğü

### Gözlemlenen Sorun
Yapısal çökme tetiklense bile çöken parçaların yerde belirgin "moloz (`LooseRubble`)" olarak kalıp kalmadığı anlaşılmıyor / gözlemlenemiyor.

### Kök Neden (2 Birleşik Hata)
1. **`RevoxelizeClusterToLooseRubble` İçinde Yanlış `ShapeID` ve Yanlış `MaterialId` Yazılması:**
   - `Assets/_Stratocraft/Physics/Systems/StructuralCollapseSystem.cs` (satır 325–336) içinde yere çarpan salkım revokselize edilirken:
     ```csharp
     bool asMicroShard = fractRatio >= 0.35f && ((wx + wy + wz) & 1) == 0;
     byte shape = asMicroShard ? VoxelBitUtility.MicroVoxelShapeId : (byte)0;
     byte mat = asMicroShard ? dominantMaterialId : MaterialIdCatalog.LooseRubble;
     ```
     - Blokların yarısı (`asMicroShard == false`) **`shape = 0` (Channel 1 Smooth Surface Nets)** olarak yazılmaktadır. Smooth Surface Nets'te mevcut arazi (`Grass/Stone`) üzerine tek başına düşen bir `shape = 0` hücresi, 8 köşesinin 7'sini arazi/hava ile paylaştığı için yumuşatılarak arazinin içinde ufak bir tümseğe dönüşür ve ayrı bir moloz yığını gibi görünmez.
     - Blokların diğer yarısı (`asMicroShard == true`) ise `shape = 63` olarak yazılmasına rağmen **`mat = dominantMaterialId` (`Stone`)** olarak bırakılmıştır! Oysa `Assets/_Stratocraft/Voxel/Jobs/HybridSurfaceNetsAndShapeMesher.cs` (satır 423):
       ```csharp
       ulong mask64 = matId == MaterialIdCatalog.LooseRubble
           ? BuildProceduralFractureShardMask64(slot12)
           : (slot12 >= 32
               ? BuildProceduralFractureShardMask64(slot12)
               : BuildProceduralButtressMask64((byte)(slot12 & 3)));
       ```
       `matId != LooseRubble` ve `PackStandard` yüzünden `slot12 = 0 < 32` olduğu için, mesher bu blokları kırık moloz parçası (`BuildProceduralFractureShardMask64`) yerine **düzgün payanda sütunu (`BuildProceduralButtressMask64`)** olarak çizmektedir!
2. **Düşen Salkımın Yere Değmeden Erken Yok Olması (`density < 64` Hava Hücreleri):**
   - `Assets/_Stratocraft/Character/Stage1StructuralCollapseBridge.cs` (`StepSingleFallingSlot`, satır 253) içinde `VoxelBitUtility.GetMaterialId(belowRaw) != MaterialIdCatalog.Air` kontrolü yapılmaktadır. Arazinin hemen üstündeki görünmez `density = 12..58 (< 64)` SDF geçiş hücreleri de `MaterialId != Air` olduğu için, düşen salkım gerçek zemine değmeden `~1m` havada çarpmış sayılıp `VisualProxy` objesi anında yok edilmektedir.

---

## 4. Rastgele Harita Üretimi, Ağaçlar ve Yapıların Olmaması

### Gözlemlenen Sorun
Haritada rastgele üretim (seed değişimi), ağaçlar ve gerçek yapılar/binalar hiç yok.

### Kök Neden
1. **Dünya Üreticisinde (`StratocraftFloatingIslandWorldGen.cs`) Hiç Seed ve Gürültü (Noise) Parametresi Olmaması:**
   - `Assets/_Stratocraft/WorldGen/StratocraftFloatingIslandWorldGen.cs` (satır 26–82) içindeki tüm fonksiyonlar (`ComputeIslandCoastRadius`, `SampleIslandTopSurfaceY`, `SampleIslandUnderbellyY`) sabit matematiksel katsayılarla (`20.8f`, `caveCenterX = -8.2f`, `calderaDx = wx - 8.2f`) çalışmaktadır; herhangi bir `worldSeed` parametresi almamaktadır.
2. **Ağaç Üretiminin Hiç Kodlanmamış Olması:**
   - `MaterialIdCatalog` içinde `OakWood (8)`, `Ironwood (9)` ve oyuncunun 3. slotunda **Demir Balta (`tool_axe_iron.glb`)** bulunmasına rağmen, ne `StratocraftFloatingIslandWorldGen.cs` içinde voksel ağaç (gövde + yaprak taçları) ne de `StratocraftStage1SceneBuilder.cs` içinde 3B ağaç yerleşimi mevcuttur.
3. **Yapıların Sadece Tek Blokluk Küçük Bir Kemerden İbaret Olması:**
   - `StratocraftFloatingIslandWorldGen.PopulateArchitecturalStructuresForChunk` (satır 293–343) sadece `(-1, -1)` chunk'ına `17` voksellik incecik bir kemer ve `(0, 0)` chunk'ına `2` adet 3 blokluk dikme koymaktadır. Üzerinde yapısal çökme (Domino) ve kuşatma testi yapılabilecek çok duvarlı bir kule, harabe bina veya tapınak yapısı üretilmemektedir.

---

## 5. Kazma İle Tam Bakılan Yerin Kazılamaması (Nişan Kayması)

### Gözlemlenen Sorun
Kazma (Slot 2) ile basılı tutarak kazarken vuruş süresi çalışıyor ancak oyuncunun tam baktığı (artı işaretinin olduğu) nokta değil, yanındaki/üstündeki alakasız bir yer kazılıyor veya yüzey hiç oyulmuyor.

### Kök Neden (3 Birleşik Hata)
1. **Bakış Yönü (`cam.transform.forward`) Yerine Üçgen Normali (`-hit.normal`) Boyunca Adım Atılması:**
   - `Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs` (`UpdateTargetAimProbe`, satır 384–404):
     ```csharp
     private static readonly float[] RayMarchOffsets = { 0.08f, 0.22f, 0.38f, 0.62f, -0.08f };
     ...
     Vector3 p = i < 3 ? (hit.point - hit.normal * RayMarchOffsets[i]) : (hit.point + cam.transform.forward * RayMarchOffsets[i]);
     ```
     `HybridSurfaceNetsAndShapeMesher` low-poly faset görünümü için köşe noktalarına deterministik jitter (`ComputeDeterministicFacetJitter`) uygular. Bu yüzden yüzeydeki üçgenlerin normali (`hit.normal`), bakış eksenine göre `25°–55°` yanlara eğiktir. `hit.point - hit.normal * 0.38f` adımı, ışını bakılan noktadan **yan voksellere (`wx ± 1, wz ± 1`)** saptırmaktadır!
2. **Görünmez (`density < 64`) Hava Tarafı SDF Hücrelerinin "Katı Blok" Sanılıp Kazılması:**
   - `Stage1MatterCrystalAndMiningController.cs` (`TryReadNonAirVoxel`, satır 406–413):
     ```csharp
     if (! TryGetGlobalVoxel(chunks, wx, wy, wz, out uint raw)) return false;
     return VoxelBitUtility.GetMaterialId(raw) != MaterialIdCatalog.Air;
     ```
     Smooth Surface Nets'te görünür yüzey `density == 64` (`SdfIsoLevel`) seviyesinden geçer. Yüzeyin hemen üstündeki voksel katmanı (`wy + 1`), SDF geçiş bandı olduğu için `MaterialId = Grass/Stone` ama **`density = 14..58 (< 64)`** değerindedir (yani fiziksel ve görsel olarak **boş havadır**!).  
     `TryReadNonAirVoxel`, `IsPhysicallySolid(raw)` (`density >= 64`) kontrolü yapmadığı için, yerin üstündeki bu **görünmez hava hücresini** yakalamakta ve oyuncu yeri kazdığını sanırken aslında yerin 1 blok üstündeki boş havayı kazmaktadır!
3. **8 Köşeli Surface Nets Hücresinde Sadece Tek Köşenin (`1x1x1`) Sıfırlanması:**
   - `ExecutePhysicalToolStrike` (satır 198) kırılan vokselde yalnızca tek bir `voxels[idx] = 0u` noktasını sıfırlamaktadır. Çevresindeki 7 komşu köşe `density = 96..127` kaldığı için Surface Nets yüzeyi o noktada tam bir çukur açmak yerine sadece `~0.15m` içeri esnemektedir; gerçek bir kazı çukuru oluşması için kırılan noktanın 6 komşusunun da SDF yoğunluğunun (`density`) yumuşakça düşürülmesi gerekir.

---

## 6. Console Loglarında (`console_get`) Tespit Edilen Ek Sorunlar

- **`Unity.NetCode.WarnAboutBatchedTicksSystem` Log Spam'i:**
  Play Mode sırasında Unity Console'a sürekli olarak şu uyarı düşmektedir:
  > `Server Tick Batching has occurred due to the server falling behind its desired SimulationTickRate...`
  - **Nedeni:** Tek oyunculu Asama 1 sahnesinde (`StratocraftStage1Showcase.unity`) NetCode paketinin otomatik oluşturduğu varsayılan ECS `ServerWorld` arka planda boş yere simülasyon tick'i çalıştırmaktadır.

---

## 7. Çözüm Planı Özeti

| # | Sorun Alanı | Uygulanacak Kesin Teknik Çözüm |
|---|---|---|
| **1** | **Sıvıların Akıp Yok Olması** | `StratocraftFloatingIslandWorldGen.cs` içinde su havuzu ve lav kalderasının taban (`y <= 6`) ve çeper (`inWaterRim`, `inLavaRim`, `isObsidianSeam`) voksellerini `density = 127` (`FullDensity`, `%100` geçirimsiz çanak) olarak üretmek; `bottomH` değerini kaldera/havuz altında `<= 3.8f` ile sınırlamak ve başlangıç sıvılarını durgun çanak içinde tutmak. |
| **2.A** | **Kılıçla Topaç Gibi Dönme** | `CameraClutchController.cs` içinde `CameraYawRadians` hesabını kümülatif konum (`WristYawRadians * dt`) yerine o karenin fare ivmesine (`smoothDeltaYaw`) ve bilek geri-çağırma yayına bağlamak; fare durduğunda dönüşün anında **`0.0 rad/sn`** olmasını sağlamak. |
| **2.B** | **Kılıcın Dik Durması** | `Stage1DirectionalCombatBridge.cs` içinde kılıcı savurma vektörünün açısına (`atan2(vy, vx)`) göre kesme düzlemine (`~75°–88°` yatay/çapraz kesme açısı) yatıracak gerçek 3B bilek-omuz kuaterniyon matematiği kurmak. |
| **2.C** | **Düşmanların Vurmadan Ölmesi** | `EvaluateSweptBladeCombat`'ı yalnızca oyuncu **aktif savurma/saplama yaparken (`IsActivelyAttacking && BladeLocalSwingSpeed > 2.2f`)** ve `_hitCooldownTimer <= 0f` iken çalıştırmak; kılıç hızını yürüme hızından arındırılmış **kamera/omuz yerel uzay hızı (`local-space blade velocity`)** ile ölçmek ve vuruş yarıçapını gerçek bıçak kalınlığına (`0.22m`) indirmek. |
| **3** | **Çöken Molozların (`LooseRubble`) Görünmesi** | `StructuralCollapseSystem.RevoxelizeClusterToLooseRubble` içinde tüm molozları `MaterialIdCatalog.LooseRubble` ve `VoxelBitUtility.PackChiseled` (`ShapeID = 63`, kırık kaya parçası maskesi) ile üretmek; `StepSingleFallingSlot` çarpışma kontrolünü `VoxelBitUtility.IsPhysicallySolid` (`density >= 64`) yapmak. |
| **4** | **Rastgele Harita, Ağaçlar ve Yapılar** | `StratocraftFloatingIslandWorldGen.cs` içine `WorldSeed` desteği, prosedürel voksel ağaçlar (`OakWood` / `Ironwood` gövdeleri + yaprak taçları, balta ile kesildiğinde Domino sistemiyle devrilen!) ve çok duvarlı yıkılabilir taş kule/harabe yapılar eklemek. |
| **5** | **Kazma Nişan Kayması ve Oyulma** | `UpdateTargetAimProbe` içinde `-hit.normal` sapmasını kaldırıp doğrudan `cam.transform.forward` boyunca `VoxelBitUtility.IsPhysicallySolid(raw)` (`density >= 64`) olan ilk gerçek katı vokseli bulmak; kazma kırdığında merkez vokseli sıfırlarken 6 komşusunun SDF yoğunluğunu da düşürerek net gözle görülür krater açmak. |
| **6** | **NetCode Console Uyarısı** | Stage 1 sahnesinde gereksiz NetCode tick batching uyarısını susturmak. |
