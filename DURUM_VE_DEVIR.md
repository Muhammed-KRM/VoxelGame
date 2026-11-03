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
| **Devir Numarası** | `#017` (Faz 1 ve Faz 2 Acımasız Teknik Denetimi ve Kök Neden Raporunun Çıkarılması) |
| **Son Güncelleme Tarihi (ISO-8601)** | `2026-11-03T12:15:00+03:00` |
| **Ortam / Donanım** | `Windows 11`, `NVIDIA GeForce RTX 3050 Ti Laptop GPU 4GB`, `16 GB RAM`, Unity 6 `6000.6.7f1` |
| **Mevcut Çalışma Aşaması** | `Faz 1 ve Faz 2 Denetimi Tamamlandı -> Sıradaki: Raporlanan Kritik Kök Nedenlerin (Kılıç, Sıvılar, Animasyon, Madencilik) Düzeltilmesine Başlanması` |
| **Aktif Test Durumu** | `42/42 .NET 10 Core PASS`, `34/34 Unity EditMode PASS`, `9/9 Python PyTest PASS`, `6/6 C++ GoogleTest PASS`, `Unity 6 Console: 0 Error / 0 Warning` |
| **Kaplama VRAM Bütçesi** | `4.14 MB / 16.000 MB` (`10/10` 3B Model + `4/4` SVOX Şablonu, `%100` Bütçe İçi, Hedef: < 16.0 MB) |
| **Çalışma Belleği (RAM)** | `< 2.5 GB` (`0 B/kare` GC Alloc) |
| **Son Kalite Kapısı** | `powershell -ExecutionPolicy Bypass -File scripts/quality-gate.ps1 -Phase 2` -> **`PASS (Exit Code: 0)`** |

---

## 2. Son Oturumda Yapılanlar (Devir #017 — Faz 1 ve Faz 2 Acımasız Teknik Denetim)

1. **`docs/FAZ1_FAZ2_GERCEK_DURUM_VE_EKSIKLER_RAPORU_V2.md` Oluşturuldu:**
   - Kullanıcının şikayetleri üzerine Faz 1 ve Faz 2 mekaniklerinin altı kazındı ve büyük çoğunluğunun "göstermelik (stub/hack)" olduğu kanıtlandı.
2. **Kritik Kök Nedenler Tespit Edildi:**
   - **Sıvılar:** Havada doğuyor ve `WakeFluidNeighborhood` hiç çağrılmadığı için uykuda kalıyor.
   - **Kılıç:** `cleavePitch` formülünde `dir.y` tamamen unutulmuş; kılıç sadece dikey sallanıyor.
   - **Animasyon:** Blender modellerinde kemik yok, karakterler `Z` ile yere yattığında diz bükmek yerine kalas gibi öne devriliyor.
   - **Madencilik:** `Mathf.Max(3, ...)` zorlaması yüzünden dünyanın en sert Obsidyeni bile `0.45` saniyede anında kırılıyor.
   - **Envanter:** Görünür bir UI (Hotbar) yok ve `I` çantasından eşya transferi kodlanmamış.
3. **Senkronizasyon:** Diğer PC'deki yerel mesh farklılıkları silinerek `#016` commit'leri güvenle çekildi (`git pull --ff-only`).

---

## 3. Önceki Oturumda Tamamlananlar (Devir #016 — Canlıların Hareket Sorunlarının Teşhisi ve Tam Çözümü)

1. **Geliştirme 1 — Canlıların Play Modunda Sahneden Otomatik Tanınması (`EnsureCreaturesInitialized` - `Stage1InteractivePlayController.cs`):**
   - **Kök Neden:** Scene Builder editörde çalışırken `playController.RegisterPatrollingCreature(...)` çağırıyordu. Ancak `Stage1CreatureSimulationBridge` C# sınıfı `[SerializeField]` olmadığı için Unity Play moduna girdiğinde `_creatureBridge._agents` listesi serileşmeyip sıfırlanıyordu (`Count == 0`). Sahne çalışsa bile içinde kayıtlı canlı bulunmuyordu.
   - **Çözüm:** `EnsureCreaturesInitialized()` metodu eklendi; oyun başında `04_Faz2_Creatures_VAT_Siege_And_PhoenixBoss` altındaki Ash Stalker'lar ve Anka Boss'u hiyerarşiden taranarak runtime'da anında canlı simülasyona kaydedildi.

2. **Geliştirme 2 — Eliptik Devriye Turu ve Yöne Doğru Dönüş (`StepPatrolAgent` - `Stage1CreatureSimulationBridge.cs`):**
   - **Kök Neden:** Canlılar sadece tek bir eksende sinüs salınımı yapıyor ve baktıkları yönü hareket yönüne göre güncellemiyordu.
   - **Çözüm:** `AnchorPos` etrafında doğal eliptik rota (`radiusX = 2.4m`, `radiusZ = 2.0m`) ve yürüdükleri teğet açısına göre otomatik yönelme (`moveYaw`), basamak tırmanma ve iki ayaklı adım sarsıntısı (`pitch`/`roll`) eklendi.

3. **Geliştirme 3 — Yakın Dövüşte Kilitlenip Kalmanın Engellenmesi (Dinamik Strafe & Lunge - `StepAggroAgent`):**
   - **Kök Neden:** Yaratık oyuncuya 2.15 metreden yaklaştığında doğrudan `MeleeAttack` durumuna geçiyor ve hiçbir pozisyon güncellemesi almayıp heykel gibi donuyordu.
   - **Çözüm:** `MeleeAttack` sırasında oyuncunun etrafında dairesel adımlama (`circling strafe`) ve saldırı darbesi sırasında öne doğru ani hamle (`lunge`) eklendi.

4. **Geliştirme 4 — Voksel Sütun Taramasının Tavan Sınırından İtibaren Güvenceye Alınması (`SampleVoxelOrTerrainHeight`):**
   - **Kök Neden:** `startY = currentY + 3` hesabı, yaratık bir anlığına adanın yüzeyinden aşağı kaydığında üstündeki katı vokselleri kaçırabiliyordu.
   - **Çözüm:** `topY = MaxEditableVoxelY - 2` (62 voksel) tavanından aşağı doğru tarama yapılarak adanın en üst katı bloğu bit-seviyesinde sıfır maliyetle kesin tespit edildi.

5. **Geliştirme 5 — Unity Arka Planda Zaman Akışının Açılması (`runInBackground = true`):**
   - **Kök Neden:** Unity editörü arka plandayken (`runInBackground: false`) `Time.time` ilerlemiyor ve oyun duraklatılmış gibi görünüyordu.
   - **Çözüm:** `Application.runInBackground = true` ayarlandı; editör arka plandayken dahi 60 FPS simülasyon ve canlı hareketleri kesintisiz devam ediyor.

1. **Geliştirme 1 — Domino Yıkımda 24 Blok Sınırının Kaldırılması & Kütle Korunumlu Pancake Katman Yığılması (`StructuralCollapseSystem.cs`):**
   - **Kök Neden:** `rubbleTarget = Math.Clamp((int)MathF.Ceiling(sourceVoxelCount * 0.65f), 1, 24)` formülü yüzünden çöken bina ne kadar büyük olursa olsun yere en fazla 24 blok moloz düşüyor, kalan yüzlerce blok buharlaşıyordu; ayrıca arama yarıçapı `r <= 2` sabitti.
   - **Çözüm:** Tavan sınırı `1024` bloğa çıkarıldı (`Math.Clamp(..., 1, 1024)`), dinamik yarıçap (`r <= maxRadius`) eklendi ve dikeyde dolan sütunların üstüne katman katman yığılma (`ty++`) ile gerçek Pancake çöküş ve kütle korunumu sağlandı.

2. **Geliştirme 2 — Akışkan (Su & Lav) Simülasyonunun ve Sıvı Yüzey Meşinin Sahnede Canlı Döngüye Bağlanması (`Stage1InteractivePlayController.cs`):**
   - **Kök Neden:** `FluidSimulationSystem` ve `FluidSurfaceSkirtMesher` sınıfları yazılmış olmasına rağmen canlı sahne kontrolcüsünde hiçbir zaman çağrılmıyordu; sahnede gölet ve lav kalderası sadece kuru çukurlardı.
   - **Çözüm:** `_fluidChunks`, `_fluidMeshFilters` ve `FluidSimulationSystem` canlı sahneye bağlandı. `Update` içinde 8 Hz hızında `StepFluidSimulation` adımlanıyor; blok kırılıp bent yıkıldığında su ve lav akıyor, karşılaştıklarında `Obsidyen (ID=11)` üretiyor ve `RebuildAllDirtyChunks` içinde `FluidSurfaceSkirtMesher` ile dinamik sıvı yüzeyi yeniden örülüyor.

3. **Geliştirme 3 — G Tuşu Quick-Drop ile Yere Beyaz Küp Yerine Eldeki Gerçek 3B Silahın/Aletin Atılması (`Stage1HolsterAndBackpackController.cs`):**
   - **Kök Neden:** `HandleQuickDropAndInteractHotkeys` metodu `GameObject.CreatePrimitive(PrimitiveType.Cube)` ile renksiz beyaz bir kutu oluşturuyordu.
   - **Çözüm:** Elden bırakılan aletin aktif slotuna göre gerçek 3B meşi (`Kılıç`, `Demir Kazma`, `Demir Balta` veya `İnşa Bloğu`) klonlanarak yere fiziksel yönüyle bırakılıyor; `E` ile geri alındığında kusursuz olarak aynı el yuvasına kuşanılıyor.

4. **Geliştirme 4 — Chunk-Snapped Floating Origin'in Canlı Sahneye Gerçekten Bağlanması (`Stage1InteractivePlayController.cs`):**
   - **Kök Neden:** `_floatingOriginChunks` listesi boştu ve kamera koordinat tuple'ı yerel kopya olarak verilip Unity Transform'ları ötelenmiyordu.
   - **Çözüm:** `EnsureChunksInitialized` içinde sahnedeki tüm aktif chunk'ların `VoxelChunkComponent` bileşenleri listeye bağlandı; `_floatingOriginSystem.UpdateFloatingOrigin` tetiklendiğinde `transform.position` ve sahnedeki tüm chunk meşleri fiziksel olarak $-(K \times 32\text{m})$ kaydırılıyor.

5. **Geliştirme 5 — Canlıların Voksel Araziyi Tanıması, Basamak Aşması (Step-Up) ve Duvarlardan Sıyrılması (`Stage1CreatureSimulationBridge.cs`):**
   - **Kök Neden:** Yaratıklar yürürken ve kovalarken voksel bloklarını görmüyor, sadece ada matematik formülünden (`SampleIslandTopSurfaceY`) yükseklik alıp blokların içine giriyordu.
   - **Çözüm:** `SampleVoxelOrTerrainHeight` metoduyla canlı voksel verisi tarandı; yaratıklar önlerinde $\le 1.25\text{m}$ basamak varsa tırmanıyor (`step-up`), önlerinde aşamayacakları yüksek bir duvar/blok varsa teğet yönüne saparak engelin etrafından dolanıyor.

1. **Çözüm 1 — Sürekli Zıplama/Ekran Titremesi (Pogo-Stick Jitter) ve Normal Zıplayamama Hatası (`Stage1PlayerMovementBridge.cs` - 292 satır):**
   - **Kök Neden:** `SampleSupportGroundHeight` yüzey yüksekliğini `y + 0.65f` döndürürken, hemen ardından çalışan `ResolveVoxelAndColliderDepenetration` metodu `footWy = FloorToInt(pos.y + 0.12f)` kontrolüyle oyuncuyu her karede `y + 1.04f`'e fırlatıyor, ertesi karede `IsGrounded = 0` olduğu için yerçekimi tekrar aşağı çekiyor ve `Space` ile zıplama engelleniyordu.
   - **Çözüm:** Zemin yüksekliği (`Physics.Raycast` + voksel tavan sınırlaması) ile gömülme kurtarma (`ResolveVoxelAndColliderDepenetration`) tek bir tutarlı referansta birleştirildi; `0.16s` `CoyoteGroundedTimer` ve `0.20s` `JumpLaunchTimer` eklendi. Karakter zeminde sıfır titremeyle pürüzsüz yürür ve `Space` tuşuyla her basışta kararlı zıplar.

2. **Çözüm 2 — Eldeki Malzemeyi Bırakma (`Q` / `4`) ve İnşa Önizlemesinin (Ghost Preview) Sürekli Ekranda Kalması (`Stage1HolsterAndBackpackController.cs`, `Stage1MatterCrystalAndMiningController.cs`):**
   - **Kök Neden:** `UpdatePlacementGhostVisual` metodu `activeSlot != 1` koşulu yüzünden elde Kazma (`2`) veya Balta (`3`) varken bile inşa önizleme küpünü ekranda tutuyordu; ayrıca `4` numaralı malzeme yuvasından çıkma kısayolu net değildi.
   - **Çözüm:** **`Q` tuşu** (veya tekrar **`4` tuşuna** basmak) eldeki malzemeyi anında bırakıp Kazmaya (`Slot 2`) döner ve önizlemeyi kapatır. İnşa önizleme hayaleti yalnızca elde malzeme varken (`Slot 4`) gösterilir; Kılıçta (`Slot 1`) tamamen gizlenir.

3. **Çözüm 3 — Malzemeleri Sadece Şekilli Değil, Doğal Arazi Haliyle (`ShapeID = 0: Doğal Hal`) Koyabilme (`Stage1MatterCrystalAndMiningController.cs`):**
   - **Kök Neden:** `ExecuteCrystalOrRubblePlacement` içinde `SelectedShapeId == 0 ? (byte)1 : SelectedShapeId` ifadesi `ShapeID = 0` (Doğal Arazi) seçili olsa bile zorla `ShapeID = 1` (Tam Küp Blok) yazıyordu.
   - **Çözüm:** Varsayılan inşa modu **`0: Doğal Hal (Organik Arazi)`** (`ShapeID = 0`, `BlokKokenDurumu.DogalKutle`) yapıldı; `Right Click` ile konulduğunda `HybridSurfaceNetsAndShapeMesher` (**Kanal 1 Surface Nets**) üzerinden mevcut araziyle `0.000m` dikişsiz organik tepecik olarak birleşir. **`V` tuşu** ile `0: Doğal Hal` ve `1..7` mimari şekiller (`Tam Küp`, `45° Rampa`, `İç/Dış Köşe`, `Kemer`, `Kilit Taşı`, `Sekizgen Sütun`, `Mikro-Blok`) arasında geçiş yapılır.

4. **Çözüm 4 — Kazma/Balta/Kristal ile Kırma Alanı Boyutunu Seçme (`Fare Tekerleği` / `T`) ve 3B Kırma Çerçevesi (`Stage1MatterCrystalAndMiningController.cs`):**
   - **Kök Neden:** `ExecutePhysicalToolStrike` fırça yarıçapını yok sayıp her zaman tek voksel kırıyordu ve yerde kırma alanını gösteren bir 3B çerçeve yoktu.
   - **Çözüm:** **Fare Tekerleği (`Scroll`)** veya **`T` tuşu** ile kırma alanı **`1x1x1 Tek Blok` -> `2x2x2 Orta Alan` -> `3x3x3 Geniş Alan` -> `5x5x5 Dev Kazı`** arasında seçilir, yerde altın sarısı 3B kırma çerçevesi büyüyüp küçülür ve kazma/balta seçilen hacmin tamamını kırar.

5. **Çözüm 5 — Karakter 3B Modelinde Baş Üstünde Duran Gövde Parçası ve Çömelme/Kalkma Uyumsuzluğu (`StratocraftStage1SceneBuilder.cs`, `Stage1ArticulatedRigAnimator.cs`):**
   - **Kök Neden:** `TacticalClothBackpack_3Tier` nesnesi `armor_slot_torso_layer3_cuirass.glb` (vertex'leri zaten `y ≈ 1.15m` göğüs hizasında olan zırh gövdesi) kullanılarak `playerRoot` altına `localPosition.y = 1.18f` ile eklenmişti (`1.18 + 1.15 = 2.33m` yükseklikte başın üstünde havada duruyordu ve çömelince `PlayerBodyRoot` ile birlikte inmiyordu).
   - **Çözüm:** `TacticalClothBackpack_3Tier` doğrudan `PlayerBody_EquippedIronVanguard` (`bodyGo`) altına sırt hizasına (`y = 0.42f, z = -0.18f`, ölçek `(0.65, 0.55, 0.50)`) bağlandı ve çömelme (`Ctrl`) `targetYOffset = 0f` olarak düzeltildi. Artık ayaklar zemine tam basar, gövde/sırt parçası çömelip kalkarken karakterle tek parça hareket eder ve FPS modunda kameranın içine girmez.

6. **Çözüm 6 — FPS Kılıç Görünümü, Kamera Sabitleme (`Camera-Clutch` Tam Kilit) ve Omuzdan Tam Ekran Kılıç Savurma Yayı (`Stage1DirectionalCombatBridge.cs`, `Stage1InteractivePlayController.cs`):**
   - **Kök Neden:** Silahlar `playerRoot` altında kalça hizasında (`y = 1.05m`) durduğu için FPS kamerasının (`y = 1.64m`) önünde görünmüyordu; `StepCombatAndCameraInput` içinde `IsClutchEngaged` aktifken bile kameraya `clutchYawDeltaDeg` eklenmeye devam ediyordu; ayrıca `ApplyInertialWeaponTransform` kılıcı kendi ekseninde döndürüyordu.
   - **Çözüm:** FPS modunda aktif silah/alet doğrudan `PlayerCamera.transform.TransformPoint(...)` ile ekranın sağ-ön görüş alanına bağlandı. Kılıç (`Slot 1`) seçiliyken `Sol Tık` veya `Sağ Tık` basılı tutulduğunda **kamera dönüşü `%100` sabitlenir (`KAMERA KİLİTLİ`)**; fareyi çekip bırakınca (veya basılı tutarken hızla savurunca) kılıç olduğu yerde dönmez, 2. derece eylemsizlik momenti (`I_weapon`) ve aerodinamik sürüklenmeyle omuz-kol yayında ekranın bir ucundan nişangah merkezini yararak karşı uca doğru tam ekran savrulur.

---

## 3. Önceki Oturumda Tamamlanan 8 Ana Mimari Paket (`docs/FAZ1_FAZ2_GERCEK_DURUM_VE_EKSIKLER_DENETIM_RAPORU.md`)

1. **Paket 1 — Anti-Burial Kapsül Çarpışma Kilidi & Fiziksel Karakter Köprüsü (`Stage1PlayerMovementBridge.cs` - 295 satır):**
   - `CanPlaceBlockOutsidePlayerCapsule`: Oyuncunun kendi kapsül hacmi (`±0.05m` güvenlik payı) içine blok koyup dünyadan düşmesini kesin olarak engeller.
   - `StepCharacterPhysics` & `ResolveVoxelAndColliderDepenetration`: `PhysicalCharacterControllerSystem.StepMovement` ve `TryInitiateSlide` sistemlerini canlı sahneye bağlar; voksel ve collider sıkışmalarını her karede dışarı iter (`RescueFromVoid` emniyeti dahil).

2. **Paket 2 — Kütle Korunumlu Domino Yıkım (`Shift+LMB` & `RMB`), `18 KB PrebakedFracturePatternLut` Çoklu Faktörlü Kırılma & `Taş Moloz (15)` (`PrebakedFracturePatternLut.cs`, `StructuralCollapseSystem.cs`, `Stage1StructuralCollapseBridge.cs` - 278 satır):**
   - Hem blok kırıldığında hem de blok eklendiğinde (`VoxelGridMemoryLayout.YMajorChunk`) `Dial's Bucket Geodesic Dijkstra` + `Single-Pass Tributary Load DAG` ile uçan/aşırı yüklü parçalar tespit edilir.
   - Kopan blok kümeleri yok olmaz; `64` yuvalı halka tamponda yerçekimiyle düşer (`>= 16` eşzamanlı kümede `Surge Mode` O(1) LUT baypası devreye girer).
   - Çarpma anında `18,432 Byte` (`18 KB`) `PrebakedFracturePatternLut` üzerinden düşme yüksekliği, malzeme sağlamlığı, kütle merkezi eksantrikliği ve darbe odaklanmasına göre $\Phi_{\text{frac}}$ hesaplanır; kırılan vokseller `ShapeID=63` düzensiz mikro-voksel parçalarına ve `LooseRubble (15)` (**Taş Moloz**) bloklarına dönüşür, alt zemini delebilir (`DoesImpactBreachFloorBlock`) ve altındaki canlı/oyuncuya kinetik ezilme hasarı verir.

3. **Paket 3 — `264-Byte Q24.8` Madde Kristali Kütle Muhasebesi, `Alt+Tekerlek` Fırça Yarıçapı (`0.5m..4.0m`), `B` Malzeme Seçimi ve Sertliğe Göre Kademeli Kırma (`Stage1MatterCrystalAndMiningController.cs` - 299 satır):**
   - `CrystalMassStorage` ve `MatterCrystalVacuumSystem` canlı sahneye bağlandı; vakumlanan her voksel gerçek `Q24.8 kg` kütlesiyle kristale eklenir, inşa ederken seçilen malzemenin (`B` tuşu) kütlesinden düşülür (kütle bitince sınırsız koyma engellenir).
   - `14` Rezonans Cevheri (`Demir #100` .. `Aetherium #113`) ve `Oyuncu İtem Bloğu (1)` kristalle vakumlanamaz (`ERR_ORE_RESONANCE_IMMUNE`); fiziksel kazma ile vuruş başına `DamageState (0..15)` artırılarak malzeme sertliğine göre kademeli kırılır.

4. **Paket 4 — Otonom 3B Kazma/Balta GLB Modelleri, `1–4` El Kılıfları, `G` `0.0s` Quick-Drop + `E` Toplama, `I` 3B Çanta Örtüsü ve `64-Bit CAS` Sandık + Örs Geri Dönüşümü (`Stage1HolsterAndBackpackController.cs` - 285 satır):**
   - `Tools/AutonomousAssetFactory/generators/weapon_hilt_generator.py` ile `tool_pickaxe_iron.glb` ve `tool_axe_iron.glb` üretildi (`10/10` 3B varlık `asset_validator.py` PASS).
   - `1`: Kılıç (`0.6s`), `2`: Demir Kazma (`0.6s`), `3`: Demir Balta (`0.6s`), `4`: Çıplak El / Madde Kristali (`0.2s`).
   - `G` tuşu eldeki aleti `0.0s` sürede fiziksel olarak yere düşürür; yerde duran alete veya `I` çanta örtüsü üzerindeki eşyalara bakıp `E` / `Sol Tık` ile kuşanılır.
   - Görsel sandığa (`VisualChest`) bakıp `E`'ye basınca `VisualChestCasSystem` (`64-Bit CAS`) çalışır ve sandık kapağı `110°` yay fiziğiyle açılır; Çift Boynuzlu Örse bakıp `E`'ye basınca `4x Taş Moloz (15) -> 1x Ham Taş (3)` geri dönüşümü yapılır.

5. **Paket 5 — 3 Parçalı Eklem Hiyerarşisi (`UpperBodyRoot` + `LowerLegsRoot`) ve Prosedürel Karakter Animatörü (`Stage1ArticulatedRigAnimator.cs` - 191 satır):**
   - `F5` TPS kamerada `Ctrl` (Çömelme / Kayma) ve `Z` (Yüzüstü Sürünme) durumlarında artık sadece kamera inmez; karakterin üst gövdesi, bacakları ve silah kolu fiziksel duruşa, yürüme salınımına ve kazma/kılıç savurma açılarına göre gerçek zamanlı eklemlenir.

6. **Paket 6 — `240 Hz` Ataletli `360°` Serbest Açı Yakın Dövüş, `OneEuroFilter3D`, `CameraClutchController` ve Stable PD Tork Amortisörü (`Stage1DirectionalCombatBridge.cs` - 215 satır):**
   - Fare hareketi `OneEuroFilter3D` ile filtrelenir, `CameraClutchController` ile `±20°` ölü bölge / `%85` sönümlü takip uygulanır ve silahın gerçek kütlesi (`MassKg`) ile ikinci dereceden Stable PD açısal yay-sönümleyici üzerinden `360°` serbest açılı ataletli savurma + `Forte/Foible` hasar/parry fiziği çalıştırılır.

7. **Paket 7 — Yaşayan 21 Canlı AI Durum Makinesi (`Patrol/Aggro/Chase/Attack`), Uzuv Koparma ve `TARGET_SUPPORT_PILLARS` Domino Tetiklemesi (`Stage1CreatureSimulationBridge.cs` - 289 satır):**
   - Sahnede heykel gibi duran 3 canlı (`ArmoredGolem`, `PackStalker`, `FlyingStrider`) artık `Creature21BehaviorSystem` ve `HybridAiLodPromotionSystem` ile devriye gezer, oyuncuyu algılar, kovalar ve saldırır.
   - Oyuncu yüksek bir sütun/kule üstüne çıkarsa canlılar `TARGET_SUPPORT_PILLARS` durumuna geçip alt sütun voksellerini kırarak Domino çökmesini tetikler; kılıç/balta darbeleri canlılara hasar verir ve uzuv koparır.

8. **Paket 8 — SRP Orkestratörü (`<= 300` Satır Kuralı), `F6/F9` `.sregion` Kayıt/Yükleme, Yüzen Orijin, `30sn` Kapalı Kutu Oksijen BFS, SQLite Telemetri ve `14` Cevher Dünya Üretimi (`Stage1InteractivePlayController.cs` - 298 satır):**
   - `1159` satırlık monolitik kontrolcü, Geliştirici Dokümanı Bölüm 4.2 kurallarına (`<= 300` satır/sınıf, `<= 40-80` satır/metot, maks 3 iç içe blok, Türkçe XML docstring, `0 B/kare` GC Alloc) tam uyumlu 7 modüler sınıfa ayrıldı.
   - `StratocraftFloatingIslandWorldGen` içine `OreDistributionTable.SampleOreMaterial` entegre edildi; `F6`/`F9` `.sregion` kaydet/yükle, `ChunkSnappedFloatingOriginSystem`, `EnclosedOxygenFloodFillSystem` ve `SqliteTelemetryLogger` canlı döngüye bağlandı.

---

## 3. Sahne ve Oynanış Durumu (Tuş Kontrolleri)

| Tuş / Eylem | Fonksiyon | Durum |
| :--- | :--- | :--- |
| **Mouse Hareketi** | FPS/TPS Bakış + `Camera-Clutch` Serbest Açı Kılıç Yönlendirme | ✅ Aktif |
| **WASD + Shift / Ctrl / Z / Space** | Yürüme, Koşu, Kayma (`3.66m`), Yüzüstü Sürünme (`0.65m`), Zıplama / `2.2m` Mantle | ✅ Aktif (`PhysicalCharacterControllerSystem` + 3B Parçalı Rig) |
| **F5** | FPS / TPS Kamera Açısı Değiştirme | ✅ Aktif |
| **1 / 2 / 3 / 4** | El Kılıfları (`1`: Kılıç, `2`: Demir Kazma, `3`: Demir Balta, `4`: Çıplak El / Kristal) | ✅ Aktif |
| **G / E** | `G`: Eldeki Aleti `0.0s` Yere At (`Quick-Drop`) \| `E`: Yerden Alet Al / Sandık Aç / Örs Geri Dönüşüm | ✅ Aktif |
| **C** | Sol Bilek Madde Kristali Modu Aç/Kapat | ✅ Aktif (`264-Byte Q24.8` Kütle Muhasebesi) |
| **Alt + Fare Tekerleği** | Madde Kristali Kazı/İnşa Fırça Yarıçapı (`0.5m .. 4.0m`) | ✅ Aktif |
| **B / V / T** | `B`: İnşa Malzemesi Seç (`Ham Taş`, `Toprak`, `Ahşap`, `Taş Moloz`) \| `V`: Şekil Çarkı (`0..7`) \| `T`: `90°` Döndür | ✅ Aktif |
| **LMB / Shift+LMB / RMB** | Kristal Vakum / Kazma Kademeli Kırma + Domino Yıkım + Kütle Korumalı İnşa (Anti-Burial Korumalı) | ✅ Aktif |
| **I** | `The Forest` 3B Kumaş Çanta Örtüsü (Üzerindeki Eşyaya Tıklayarak Kuşanma / Kristale Aktarma) | ✅ Aktif |
| **F6 / F9** | `.sregion` + `CrystalMassStorage` + Karakter Durumu Hızlı Kaydet (`F6`) / Hızlı Yükle (`F9`) | ✅ Aktif |

---

## 4. Sıradaki İlk İş (Yeni Oturum / Yeni Bilgisayar İçin)

1. **Adım 1 — Repoyu ve Kalite Kapısını Doğrula:**
   ```powershell
   git status -s
   git log -5 --oneline --date=iso
   powershell -ExecutionPolicy Bypass -File scripts/quality-gate.ps1 -Phase 2
   ```
2. **Adım 2 — `FAZ1_FAZ2_GERCEK_DURUM_VE_EKSIKLER_RAPORU_V2.md` İçindeki Kritik Hataları Temizleme:**
   - **Öncelik A (Savaş):** `Stage1DirectionalCombatBridge.cs` dosyasını açıp serbest yönlü Mordhau/Exanima tarzı kılıç savurmasını ve `Camera-Clutch` kısıtlamasını düzelt.
   - **Öncelik B (Sıvılar & Yıkım):** Su/Lav'ın çukura yerleşmesini ve `WakeFluidNeighborhood` ile akmasını sağla. Zemin yıkım sabitlemesini kaldır.
   - **Öncelik C (Madencilik & Aletler):** 0.45 saniye kırma zorlamasını kaldır, Anayasal sertlik katsayılarını bağla ve yere bırakılan eşyaların (Fiziksel Kılıflar) silinmesi hatasını gider.
   - **Faz 3.3:** 3B Çit BFS Klan Koruma Alanı (`KGP`, Kristal HP ve 3-yönlü `IsSiegeable`) hesaplaması.
