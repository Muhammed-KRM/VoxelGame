# DOK2 — Mevcut Kod Debug ve Akış Raporu (Şu Anki Sistem Nasıl Çalışıyor & 7 Kritik Uyuşmazlığın Analizi)

> **4'lü Dokümantasyon Seti Üst Bağlantıları:**
> - **[DOK1 — Hedef ve Gereksinimler](file:///d:/oyun/docs/gercekci-cok-katli-kutuk-ev-ve-marangozluk/DOK1_HEDEF_VE_GEREKSINIMLER.md)**
> - **[DOK2 — Mevcut Kod Debug ve Akış Raporu (Bu Doküman)](file:///d:/oyun/docs/gercekci-cok-katli-kutuk-ev-ve-marangozluk/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md)**
> - **[DOK3 — Endüstri, Gerçek İnşaat Mühendisliği ve Benzer Projeler Araştırması](file:///d:/oyun/docs/gercekci-cok-katli-kutuk-ev-ve-marangozluk/DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md)**
> - **[DOK4 — Adım Adım Geliştirme ve Canlı Doğrulama Planı](file:///d:/oyun/docs/gercekci-cok-katli-kutuk-ev-ve-marangozluk/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md)**

---

## 1. Mevcut Çağrı Zinciri ve Veri Akışı (Adım Adım Kod Yürütümü)

Her oyun karesinde (`Update`) kütük kesme, taşıma, yerleştirme ve fizik çözümü şu sırayla çalışmaktadır:

1. **Ana Orkestratör Girişi:**
   - [Stage1InteractivePlayController.cs](file:///d:/oyun/Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L1344-L1380) içindeki `Update()` sırasıyla `HandleCameraModeAndPersistenceHotkeys()` (`[F2]` test seti `L4568`), `StepMovementAndOxygen(dt)` (`L1364`), `StepHolsterBackpackAndMining(dt)` (`L1365`) ve `StepCombatCreaturesAndRig(dt)` (`L1366`) metodlarını çağırır.
2. **1:1 Kütük & Şantiye Önizleme Adımı:**
   - `StepMovementAndOxygen(dt)` içinde [Stage1InteractivePlayController.cs](file:///d:/oyun/Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L3095-L3122) satırlarında `_timberPreviewCtrl.StepPreviewAndPlacement(...)` ve `_timberPreviewCtrl.StepHandAssembly(...)` çağrılır.
   - [Stage1TimberPlacementPreviewController.cs](file:///d:/oyun/Assets/_Stratocraft/Character/Stage1TimberPlacementPreviewController.cs#L662-L665) içinde `logCartCtrl.TryPeekBoundLog(out float cartLogLen, out float cartLogDiam, out byte cartWoodMat)` çağrılarak arkada çekilen arabada yüklü kütük varsa `effectiveKind = TimberBuildPreviewKind.RealLogFromCart` seçilir.
   - `[Sol Tık]` basıldığında (`L779-L796`):
     - `_logVerticalMode == true` ise `rigidTimberBridge.TryTipInLogIntoHole(...)` ([Stage1RigidTimberBridge.cs](file:///d:/oyun/Assets/_Stratocraft/Character/Stage1RigidTimberBridge.cs#L150-L275)) çağrılır; `TimberTipInAndJoineryLogic.EvaluateTipInHole(...)` ([TimberTipInAndJoineryLogic.cs](file:///d:/oyun/Assets/_Stratocraft/Physics/RigidTimber/TimberTipInAndJoineryLogic.cs#L40-L99)) 3 halkada (`0.85m, 1.65m, 2.35m`) çukur derinliğini ölçer ve `AddCylinderPieceInternal(RigidTimberPieceKind.Post, ...)` ile sekizgen prizma direk oluşturur.
     - `_logVerticalMode == false` ise `rigidTimberBridge.PlaceHorizontalLogOrBeam(...)` ([Stage1RigidTimberBridge.cs](file:///d:/oyun/Assets/_Stratocraft/Character/Stage1RigidTimberBridge.cs#L549-L619)) çağrılır.
3. **Fiziksel Balta Darbesi Akışı:**
   - 240 Hz kılıç/balta savurması temas ettiğinde `Stage1RigidTimberBridge.TryPhysicalBladeSwingHitDebris(...)` ([Stage1RigidTimberBridge.cs](file:///d:/oyun/Assets/_Stratocraft/Character/Stage1RigidTimberBridge.cs#L1099-L1191)) çağrılır.
   - Vurulan parça `StaticAnchored` ise önce mevcut `CornerBond4Interface` bağlarına `<= 0.95m` yakınlık kontrol edilir (`L1145-L1164`); yakınsa `UpgradeJoineryWithAxeNotch` ile `FrictionOnly -> SaddleNotch -> Dovetail` yükseltilir. Gövdeye vurulduysa `Damage01 += 0.36f` birikir ve 3. darbede `TrySplitPieceAtWorldPoint` (`L1198-L1399`) parçayı tam vurulan noktadan ikiye böler.
4. **Statik Denge ve Oyuncu Çarpışması:**
   - Parça eklendiğinde/kesildiğinde `ReevaluateStaticEquilibrium` ([Stage1RigidTimberBridge.cs](file:///d:/oyun/Assets/_Stratocraft/Character/Stage1RigidTimberBridge.cs#L1675-L1699)) üzerinden `TimberStaticEquilibriumSolver.EvaluateDirtyIsland` ([TimberStaticEquilibriumSolver.cs](file:///d:/oyun/Assets/_Stratocraft/Physics/RigidTimber/TimberStaticEquilibriumSolver.cs#L46-L355)) çalışır.
   - Oyuncu yürümesi/çarpışması `Stage1RigidTimberBridge.ResolvePlayerCollision` ([Stage1RigidTimberBridge.cs](file:///d:/oyun/Assets/_Stratocraft/Character/Stage1RigidTimberBridge.cs#L1040-L1090)) içinde 3 iterasyonlu OBB çözücü ile yürütülür.

---

## 2. Tespit Edilen 7 Kritik Kod Uyuşmazlığı + İç İçe Geçme Kök Nedenleri

### Kök Neden 0 — İç İçe Geçme (*Clipping*) ve Düz Silindir Geometrisi
- **Dosya & Satır:** [Stage1RigidTimberBridge.cs](file:///d:/oyun/Assets/_Stratocraft/Character/Stage1RigidTimberBridge.cs#L486-L508) ve [Stage1RigidTimberBridge.cs](file:///d:/oyun/Assets/_Stratocraft/Character/Stage1RigidTimberBridge.cs#L1858-L1876) (`EmitLogPrism`)
- **Mevcut Davranış:**
  1. `EmitLogPrism` yalnızca düz, oyuksuz 8-gen silindir üretmektedir; ne direk yüzeyinde dikey oluk (*Groove*), ne yan kiriş zıvanası (*Mortise*), ne de yatan kütükte yarım ay oyuk (*Saddle Notch*) vardır.
  2. Yatay kütük konurken arabadaki tam kütük boyu (`realLen`, örn. `12m`) kesilmeden olduğu gibi konmakta ve direk merkezleri baz alındığı için yatay kütük dikey direklerin gövdesinin **içinden boydan boya geçmektedir**.
  3. `[R]` tuşu [Stage1TimberPlacementPreviewController.cs](file:///d:/oyun/Assets/_Stratocraft/Character/Stage1TimberPlacementPreviewController.cs#L702-L705) içinde `_previewYawDeg = (_previewYawDeg + 45f) % 360f` ile sadece `45°` katlarında döndüğü için serbest açılı iki direğe elle hizalamak imkânsızdır.

---

### Uyuşmazlık 1 — `[T] -> [3]` (`WorldLooseBuildSystem`) İle `Stage1RigidTimberBridge` İki Kopuk Sistemdir
- **Dosya & Satır:** [Stage1WorldLooseBuildController.cs](file:///d:/oyun/Assets/_Stratocraft/Character/Stage1WorldLooseBuildController.cs#L2468-L2617) (`GatherNearbyUnifiedNeighbors`, `HasSupportRoleNear`)
- **Teknik Analiz:**
  - `WorldLooseBuildSystem` yalnızca `UnifiedGraphSolver.NodesSpan` içindeki `StructuralNode64` düğümlerini tarar ve yönelimini `Quaternion.Euler(0f, yawQuarters * 90f, 0f)` (`L2472`) ile `90°` eksenlere kilitler.
  - `Stage1RigidTimberBridge._pieces` dizisindeki hiçbir direği (`Post`) veya kirişi (`Beam`) görmez. Bu nedenle oyuncunun serbest açıda diktiği ana direklerin üstüne `[T] -> [3]` menüsünden kat döşemesi, merdiven veya çatı koymaya çalışması hem destek doğrulamasında reddedilir hem de geometrik olarak örtüşmez.

---

### Uyuşmazlık 2 — `TimberStaticEquilibriumSolver` İçindeki `anchoredPostCount <= 3` Erken Yıkım Hatası
- **Dosya & Satır:** [TimberStaticEquilibriumSolver.cs](file:///d:/oyun/Assets/_Stratocraft/Physics/RigidTimber/TimberStaticEquilibriumSolver.cs#L187-L223)
- **Teknik Analiz:**
  ```csharp
  if (visitedCount >= 10 && (maxY - minY) >= 4.5f && anchoredPostCount >= 1 && anchoredPostCount <= 3 && totalMass > 1.0)
  ```
  - Bu blok, 4 köşeli hazır pilot evin (`SpawnThreeStoryLogCabinPilot`) 1 köşe sütunu kesildiğinde kalan 3 sütun üzerinde devrilmesi için yazılmıştır.
  - Ancak oyuncu hayatta kalma modunda `12 m` tam boy direk diktiğinde `maxY - minY >= 4.5f` koşulu **ilk direkten itibaren** doğru olur! Oyuncu henüz 2 veya 3 direk dikip (`anchoredPostCount <= 3`) aralarına 8–9 parça duvar kütüğü sürdüğünde (`visitedCount >= 10`) ve `eccentricity >= 0.35f` olduğunda (`L197`, örn. L-köşe duvar örerken ağırlık merkezi köşe ortasından kaydığında), çözücü tüm bağları (`bonds[b].IsActive = 0`, `L209`) koparıp **inşa halindeki sağlam duvarı kendi kendine yıkar!**

---

### Uyuşmazlık 3 — `RigidTimber` Görsellerinde `Collider` Yok → `miningCtrl.AimHit` Direkleri Iskalar
- **Dosya & Satır:** [Stage1RigidTimberBridge.cs](file:///d:/oyun/Assets/_Stratocraft/Character/Stage1RigidTimberBridge.cs#L1824-L1856) & [Stage1TimberPlacementPreviewController.cs](file:///d:/oyun/Assets/_Stratocraft/Character/Stage1TimberPlacementPreviewController.cs#L716-L718)
- **Teknik Analiz:**
  - `BuildCylinderVisual` ve `BuildBoxVisual` performans için Unity PhysX `MeshCollider` eklemez.
  - Ancak `StepPreviewAndPlacement` hedef noktayı `miningCtrl.AimHit` (PhysX raycast) ile alır (`L716`). Sonuç olarak oyuncu direğin `3.2 m` yukarısına (kiriş yuvası açmak veya üst kat kirişi koymak için) ya da `2.2 m` kapı lento hizasına baktığında ışın direğin içinden geçip arkadaki araziyi vurur.
  - Oysa [Stage1RigidTimberBridge.cs](file:///d:/oyun/Assets/_Stratocraft/Character/Stage1RigidTimberBridge.cs#L1909-L1944) içinde hazır 0-GC `TryRaycastPieceObb` metodu zaten mevcuttur, fakat nişangâh/önizleme hattına bağlanmamıştır!

---

### Uyuşmazlık 4 — Kütük El Arabası ve Yerdeki Devrik Ağaçlarda `int` Tam Metre Yuvarlaması
- **Dosya & Satır:** [Stage1LogArchCartController.cs](file:///d:/oyun/Assets/_Stratocraft/Character/Stage1LogArchCartController.cs#L30,L192-L195,L258) & [Stage1StructuralCollapseBridge.cs](file:///d:/oyun/Assets/_Stratocraft/Character/Stage1StructuralCollapseBridge.cs#L3037-L3056)
- **Teknik Analiz:**
  - `LogArchCartRuntime.BoundSegments` (`int`) ve `SpawnFullLogFromCart(..., int segments)` kütük boyunu tam sayı metreye yuvarlar.
  - İki oluklu direk arasındaki net yüzeyden yüzeye açıklık `3.42 m` olduğunda ve `11.00 m` kütükten kesildiğinde kalan `7.58 m` kütük `int` yüzünden `7 m` veya `8 m` olarak bozulur. `FallenTreeLogInstance` içinde `SegmentLengthMeters` (`float`, `L1541`) zaten bulunduğu halde `SpawnPartialFallenTreeLog` bunu sabit `1.0f` (`L3122`) atamaktadır.

---

### Uyuşmazlık 5 — Yerleştirme Önizlemesinin Sadece El Arabasına Bağlı Olması (Üst Kat Çıkmazı)
- **Dosya & Satır:** [Stage1TimberPlacementPreviewController.cs](file:///d:/oyun/Assets/_Stratocraft/Character/Stage1TimberPlacementPreviewController.cs#L662) & [Stage1TimberSiteRigController.cs](file:///d:/oyun/Assets/_Stratocraft/Character/Stage1TimberSiteRigController.cs#L229-L255) & [Stage1HolsterAndBackpackController.cs](file:///d:/oyun/Assets/_Stratocraft/Character/Stage1HolsterAndBackpackController.cs#L184-L196)
- **Teknik Analiz:**
  1. `StepPreviewAndPlacement` yalnızca `logCartCtrl.TryPeekBoundLog` doğruysa kütük önizlemesi açar; oysa el arabası merdivenden 2. ve 3. kata çıkamaz.
  2. `Stage1HolsterAndBackpackController.IsCarryingShoulderLog` (`L184`) omuzda kısa kütük taşıma durumunu tutar ama `StepPreviewAndPlacement` bunu hiç okumaz.
  3. `Stage1TimberSiteRigController` içinde Parbuckle Rampa / Gin Pole Vinç `HoistState.IsCompleted != 0` olduğunda (`L229`) kütüğü oyuncunun hedeflediği iki oluklu direk arasına vermek yerine sabit `rig.YawDeg + 90f` açısıyla rampanın ağzına rastgele yatay bırakır (`L246`).

---

### Uyuşmazlık 6 — `[T] -> [7]` Zemin Kilidi, Sabit `1.90 m` Kapı ve Sabit Boylu Şömine Bacası
- **Dosya & Satır:** [Stage1TimberPlacementPreviewController.cs](file:///d:/oyun/Assets/_Stratocraft/Character/Stage1TimberPlacementPreviewController.cs#L720-L736) & [Stage1TimberInteriorController.cs](file:///d:/oyun/Assets/_Stratocraft/Character/Stage1TimberInteriorController.cs#L62-L84,L378-L450)
- **Teknik Analiz:**
  1. `[T] -> [7]` plan yüksekliği yalnız `SampleTerrainY` (voksel arazi) ile hesaplanır (`L725`); `RigidTimber` kat döşemelerinin (`Plank`) üst yüzeyi hesaba katılmaz ve `[Q]` ile bırakılan `LoosePartPoint` parçaları kat döşemesini görmez. Pencere kepengi de pencere boşluğu yerine toprağa iner.
  2. `ResolvePlayerDoorCollision` (`L71`) ve kapı mesh'i sabit `1.90 m` genişliktedir; oyuncunun diktiği iki kapı direği arasındaki net açıklığa uyum sağlamaz.
  3. `StoneFireplaceWithChimney` bacası sabit yüksekliğe sahiptir; 2–3 katlı binada çatıya ulaşamaz.

---

### Uyuşmazlık 7 — Sahte Termal Oda Sayıları ve Karakter Biyolojisi Kopukluğu
- **Dosya & Satır:** [Stage1TimberInteriorController.cs](file:///d:/oyun/Assets/_Stratocraft/Character/Stage1TimberInteriorController.cs#L322-L333) & [Stage1InteractivePlayController.cs](file:///d:/oyun/Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L3313-L3330)
- **Teknik Analiz:**
  - `RecomputeThermalState()` içinde `TotalWallLogs = 20` ve `HasRoofOverhead = 1` sabit kodlanmıştır; gerçekte etrafta duvar veya çatı olup olmadığı denetlenmez.
  - Ayrıca kütük birleşimlerine kil/lif sürüldüğünde (`L160-L170`) duvar kütükleri üzerinde hiçbir **3B görsel harç şeridi (*Chinking*)** oluşmaz ve hesaplanan sıcaklık/rüzgâr koruması oyuncunun gerçek üşüme/rüzgâr fiziğine (`_currentPlayerWindDragForceN` & `_ambientTempCelsius`) uygulanmaz.
