# DOK2 — Mevcut Kod Debug ve Akış Raporu (Şu Anki Sistem Nasıl Çalışıyor ve Neden İstediğimiz Gibi Değil?)

> **Belge Seti Navigasyonu:**  
> [`DOK1 — Hedef ve Gereksinimler`](DOK1_HEDEF_VE_GEREKSINIMLER.md) | **[`DOK2 — Mevcut Kod Debug ve Akış Raporu`](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md)** *(Bu belge)* | [`DOK3 — Endüstri, Simülasyon, Açık Kaynak ve YouTube Araştırması`](DOK3_ENDUSTRI_SIMULASYON_OSS_YOUTUBE_ARASTIRMASI.md) | [`DOK4 — Tam Akış Diyagramı ve Matematiksel Model`](DOK4_TAM_AKIS_DIYAGRAMI_VE_MATEMATIK_MODELI.md) | [`DOK5 — Adım Adım Geliştirme ve Doğrulama Planı`](DOK5_ADIM_ADIM_GELISTIRME_VE_DOGRULAMA_PLANI.md)

---

## 1. Kullanılan Teknolojiler ve Paket Envanteri (Gerçek Durum)

- **Unity Sürümü:** `6000.6.7f1` (`ProjectSettings/ProjectVersion.txt:1`)
- **Kurulu Paketler (`Packages/manifest.json:2-28`):**
  - `com.unity.burst` (`2.0.0`)
  - `com.unity.collections` (`6.6.0`)
  - `com.unity.entities` (`6.6.0`)
  - `com.unity.mathematics` (`1.4.0`)
  - `com.unity.physics` (`6.6.0`)
  - `com.unity.modules.physics` (`1.0.0` — Unity PhysX)
  - `com.unity.render-pipelines.universal` (`17.6.0`)

> [!WARNING]
> **Kritik Teknoloji Bulgusu:** Projede `Burst`, `Collections`, `Entities` ve `Unity.Physics` paketleri kurulu olmasına rağmen; mevcut yapı, inşaat, çökme ve kütük kodlarının **hiçbirinde** `[BurstCompile]`, `NativeArray`, `Unity.Physics` veya PhysX `FixedJoint` / `ConfigurableJoint` / `HingeJoint` **kullanılmamaktadır** (`0` eşleşme). Tüm yapısal çözücüler (hem `.NET 10` standalone testleri hem Unity uyumu için) **saf C# (`Span<T>`, `[ThreadStatic]` tamponlar, `System.Numerics.Vector3`)** olarak yazılmıştır.

---

## 2. Alt Sistemlerin Adım Adım Mevcut Kod Akışı ve Kök Nedenleri

### 2.1. Ağaçlar ve Kütük Kesme / Boylama Sistemi
- **Ağaç Üretimi (`Assets/_Stratocraft/WorldGen/StratocraftFloatingIslandWorldGen.cs`):**
  - Çağrı zinciri: `PopulateIslandTreesForChunk` (`L3490`) → `StampSurfaceFloraAndLandmarksInternal` (`L3501–3535`) → `BuildVoxelTree` (`L4092–4147`) → `TryResolveNaturalTreeCandidate` (`L4012–4090`).
  - Gövde yüksekliği `effectiveTrunkH = Math.Clamp(trunkHeight, 5, 10)` (`L4034`) ile sınırlıdır ve `1×1` voksel sütununda `dy = 0..effectiveTrunkH` aralığında `shapeId: 2` (`OctagonalColumn`), `BlokKokenDurumu.DogalKutle` olarak damgalanır (`L4139–4140`).
  - Taç/dal görselleri ve Euler-Bernoulli konsol dal esnemesi `Assets/_Stratocraft/WorldGen/OrganicTreeCanopyVisualSystem.cs` (`L55–202`, `OrganicTreePlacementSpec`, `OrganicTreeBranchNode`) tarafındadır.
- **Devrilme ve Boylama (`Assets/_Stratocraft/Character/Stage1StructuralCollapseBridge.cs`):**
  - Taban vokseli kesildiğinde `GeodesicSupportDijkstraJob` ağacı devirir; yere çarptığında `SpawnGroundedTreeLogForHarvesting` (`L2462–2575`) çağrılır.
  - `segments = Mathf.Clamp(slot.Cluster.MaxBounds.Y - slot.Cluster.MinBounds.Y, 5, 8)` (`L2475–2476`) ile `5–8 m` uzunluğunda `FallenTreeLogInstance` (`L1528–1541`) oluşur.
  - **Sabit `2 m` Zorlaması:** `TryChopGroundedTreeLog` (`L2646–2861`) balta darbesiyle (`L2772–2848`) **her zaman sabit `2 m`** uzunluğunda `SpawnBuckedTimberLog` (`IsBuckedTimberLog = true`) üretir.
  - `TryPickupBuckedTimberLog` (`L2866–2911`) yalnızca `IsBuckedTimberLog == true` olan sabit `2 m` kütükleri omuza alır.
  - Çap sabitleri (`Assets/_Stratocraft/Inventory/Systems/SotfLogStackAndInputSystem.cs:208–221`): `TimberLogTargetDiameterMeters = 0.44f`, `TimberLogCourseStepYMeters = 0.42f`, `TimberLogHalfExtentXZ = 0.22f`.
- **Neden İstediğimiz Gibi Değil?**
  - 3 katlı kütük evin ana sütunları (`4–6 m`) veya uzun döşeme/mahya kirişleri için tam boy / değişken boy kütük üretilemiyor; her ağaç zorla `2 m` parçalara bölünüyor ve ağaç türü/çapına göre gerçekçi kütle/mukavemet taşımıyor.

---

### 2.2. El Arabası (`Stage1WheelbarrowController.cs`)
- **Dosya:** `Assets/_Stratocraft/Character/Stage1WheelbarrowController.cs` (`L19–350+`).
- **Mevcut Akış:**
  - `WheelbarrowRuntimeEntry` (`L23–33`), `MaxCapacity = 36` küçük yuva (`WheelbarrowCargoBuffer`, `CargoSlot_0..35`, `L165–185`) tutar.
  - `IsStorableInWheelbarrow(InventoryItemKind kind)` (`L56–79`) yalnızca küçük yığın eşyaları kabul eder (`LooseDirtClod`, `SandPile`, `ClayLump`, `RoughStoneChunk`, `OreChunk`, `SplitFirewood`, `Stick`, `CharcoalChunk`, `MetalIngot` vb.).
  - PhysX `Rigidbody`/`Collider` kullanmaz ("sıfır-Collider ışın-koni etkileşimi", `L19`, `HasAimedWheelbarrow` `L190–296`); `PushForwardOffsetMeters = 1.35f` (`L36`) ile oyuncunun önünde transform olarak kaydırılır.
- **Neden İstediğimiz Gibi Değil?**
  - **Tam boy kütük (`2 m`, `4 m`, `6 m`) taşıyamıyor.** Oyuncu tek seferde yalnızca omuzda 1 adet `2 m` kütük taşıyabiliyor (`Stage1WorldLooseBuildController.cs:565–678`).

---

### 2.3. Üç Parçalı Kopuk İnşaat Dünyası ve "Birleştirme / Yutma" Hatası
Mevcut kod tabanında birbiriyle tam birleşmemiş **3 ayrı inşaat sistemi** aynı anda yaşamaktadır:

| Sistem | Ana Dosyalar | Nasıl Çalışıyor? | Temel Sorun |
| :--- | :--- | :--- | :--- |
| **1. Voxel / Kit-Stamp (Dünya Binaları)** | `VoxelSettlementArchitectureStamp.cs`, `GlbVoxelKitTemplateBank.cs` | Binaları doğrudan `uint[] voxels` + `MicroVoxelPool4096` içine `1 m³` blok/kit üçgenleri olarak damgalar. | Tekil cisim kimliği yok; `FullMasonryCube` küp görünümü kalıyor; `Math.Max(4, f.HalfX)` (`L371–372`) yüzünden evler iç içe geçiyor. |
| **2. Modüler Parça Grafı (`ModularPlacedPieceNode`)** | `Stage1WorldLooseBuildController.cs` (`L121–126`, `L1855–2065`), `ValheimSotfModularGraphSolver` | `_modularNodes[256]` içinde hazır paneller (`CompletedCourses / RequiredCourses`) yerleştirir; `BoxCollider` ekler ama `Rigidbody` yok. | **Birleştirme/Yutma Hatası (`L575–611`, `L1878–1945`):** Omuzdaki kütüğü koyunca kütük ayrı kalmıyor, `targetNode.CompletedCourses` sayacına yutulup tek bütünleşik duvar mesh'ine dönüşüyor! |
| **3. Serbest `2 m` Kütük Listesi (`_parts`)** | `Stage1WorldLooseBuildController.cs` (`L38–51`, `L720–735`, `L1213–1253`), `SotfLogStackAndInputSystem.cs` (`L687–860`) | Omuzdan serbest bırakılan kütükleri `_parts` listesine ekler; `ComputeSotfFreeformSnapPose` ile geometrik `+0.42 m Y` veya `+2.0 m` dikey snap yapar. | Kütükler arası gerçek temas/kertik/tork bağı yok; `UnifiedStructureGraphSolver` köprüsü (`_unifiedBridge`) aktif kütük fiziğini yönetmiyor. |

---

### 2.4. Çökme ve Fizik Çözücüleri (Neden Devrilmiyor, Neden Havada Kalıyor?)

1. **PhysX Arazi Collider'larının Tamamen Kapalı Olması (Kritik Kök Neden):**
   - `Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs:131`:
     `private static readonly int NearPhysicsColliderRadius = -1;`
   - Voksel arazi chunk'larında PhysX `MeshCollider` **tamamen devre dışı** bırakıldığı için, `Stage1StructuralCollapseBridge.cs:2555–2561` içinde devrilen ağaç kütüklerinin `Rigidbody`'leri dahi zorunlu olarak **`rb.useGravity = false; rb.isKinematic = true;`** yapılmıştır (aksi halde kütük zeminin içinden geçip sonsuzluğa düşer).
2. **Serbest Kütüklerde `AngularVelocity` Hiç Kullanılmıyor (Sıfır Devrilme!):**
   - `SotfLogStackAndInputSystem.cs:1377–1480` (`RecomputeLooseLogSupportAndTriggerFalls`): Mesafe tabanlı BFS (`horizDistSq <= 1.25^2`) ile destek kontrolü yapar ve düşecek kütüğe `AngularVelocity` atar (`L1473`).
   - **Hata:** `StepLooseLogsUnderGravity` (`L1485–1526`) içinde **`AngularVelocity` HİÇ KULLANILMAZ!** Yalnızca `Velocity.Y -= 9.81f * dt` ile dikey aşağı indirip `floorY`'de `Velocity = Zero` yaparak dondurur (`L1502–1517`). `Stage1WorldLooseBuildController.cs:1242–1251` de sadece `Position`'ı günceller, `Rotation`'a dokunmaz. Kütükler asla eğilmez, yuvarlanmaz, devrilmez.
3. **Toprağa Gömme, Zemin Direnci, CoM, Eğilme ve Esneme Yokluğu:**
   - `SotfLogStackAndInputSystem.cs:715–718`: Dikey sütun her zaman `baseGroundY + 1.00f` (yüzeyin tam üstüne) konur; `embedDepth` = `0`.
   - `StructuralNode64` (`UnifiedStructureTypes.cs:453–486`) yalnızca `YawQuarterTurns` (90° adımlı) ve kullanılmayan bir `FoundationDepthDm` byte alanı taşır.
   - `UnifiedStructureGraphSolver.cs` (`SolveSingleConnectedIsland`, `L1816–2191`) skaler `GeoSupportDist` + yukarıdan aşağıya düşey yük + skaler `StressRatio (eta)` hesaplar; sürekli 3B ağırlık merkezi (CoM), devrilme momenti, plastik eğilme veya dinamik çarpışma yanıtı yoktur.
4. **Voxel Domino Çözücüsü (`StructuralCollapseSystem.cs`) Neden Kırılan Evin Üst Katını Havada Bıraktı? (`PlayTest Foto 6`):**
   - `StructuralMaterialProps.cs:268–324`: `WoodPlank` (`MaxCantileverSpanBlocks = 5`), `TreatedTimberBeam` (`6`), `CutAshlarStone` (`6`) değerleri tek bir yan duvardan `5–6 m` boyunca koca kat döşemesini konsol olarak havada tutabiliyor.
   - `Stage1StructuralCollapseBridge.cs:884` (`PrepareHaloSolveScratch`): Kırılan noktadan `8 m` (`distSqXZ <= 64`) dışındaki blokları `KaliciYapi` olarak sabitliyor; büyük binaların karşı duvarı yapay ankraj görevi görüp üst katı havada tutabiliyor.
5. **PlayTest Kapı & Zıplama Çarpışma Hatası (`Stage1PlayerMovementBridge.cs:1847–1852`):**
   - `IsHardCollisionVoxelPure` (`L291`) açık kapıyı ve tavan kirişini (`IsPassableWalkthroughKit`) geçirirken, canlı harekette çağrılan `private static bool IsHardCollisionVoxel` (`L1847`) bu kontrolü **yapmıyor** — bu yüzden açık kapıya çarpılıyor ve `floorY + 4` tavan kirişine zıplayınca havada kafa vuruluyor.

---

### 2.5. Mevcut Telemetri ve Loglama Altyapısı (Yeni Fizik Loglaması İçin Hazır Zemin)
Proje güçlü ve **0-GC / main-thread bloklamayan** bir telemetri altyapısına zaten sahiptir:
1. **3 Katmanlı SQLite WAL Telemetri (`Assets/_Stratocraft/Core/Logging/SqliteTelemetryLogger.cs` & `IGameLogService.cs`):**
   - `Stage1InteractivePlayController.cs:543, 809, 1341` içinde başlatılır (`Logs/telemetry_logs.db` + `Logs/logs/stratocraft-YYYYMMDD.log`).
   - `10.000` kapasiteli `ConcurrentQueue<TelemetryEntry>` (`L22, L88`) + arka plan `StratocraftTelemetryWalWriter` iş parçacığı (`L133–138`) + native SQLite WAL köprüsü (`L126`).
   - Hazır kanallar: `LogNetworkAction` (`L142`), `LogFunctionError` (`L171`, otomatik `[CallerMemberName]/[CallerFilePath]/[CallerLineNumber]`), `LogPipelineStart/End/Warning` (`L204–250`, mikrosaniye süre + JSON metadata).
2. **Yüksek Frekanslı Kare/Substep Trace Kalıpları:**
   - `Assets/_Stratocraft/Core/Logging/WaveADeepDiagnosticProbe.cs` (`L10–80+`): Kare/substep bazlı yapılandırılmış struct kaydı.
   - `Assets/_Stratocraft/Physics/Diagnostics/FluidTraceRecorder.cs` (`L13–70+`): 0-GC `StringBuilder` ile CSV/log kaydı.
- **Çıkarım:** Yeni tek-parça yapı fiziği için aynı 0-GC kalıpla bir **`RigidTimberPhysicsTraceRecorder`** + kritik olaylarda `IGameLogService` (`telemetry_logs.db`) bağlantısı kurmak hem performansı sıfır etkiler hem de her kütük/sütun kararının (gömülme, zemin direnci, CoM, tork, eğilme açısı, kopan köşe bağı) tam izini çıkarır.

---

## 3. Özet: Neden Mevcut Sistemde İstediğimiz Gerçekçilik Oluşmuyor?

1. **Tek-parça rijit/elastik cisim katmanı yok:** Yapılar ya 1 m³ voxel'e damgalanıyor ya da `ModularPlacedPieceNode` içine yutuluyor; serbest `_parts` kütüklerinde ise dönüş/devrilme entegrasyonu (`AngularVelocity`) kapalı.
2. **Ağaç/kütük boyları ve taşıma kilitli:** Tüm kesimler sabit `2 m`, el arabası tam boy kütük kabul etmiyor.
3. **Geoteknik gömme ve ağırlık merkezi dengesi yok:** Sütunlar yüzeyin üstüne konuyor (`embedDepth = 0`), zemin türüne göre devrilme momenti ve plastik yamulma hesaplanmıyor.
4. **PhysX arazi collider'ları kapalı (`NearPhysicsColliderRadius = -1`):** Standart PhysX `Rigidbody` kullanılamıyor (ve kullanılsa bile `DOK3`'te kanıtlandığı üzere PhysX `ConfigurableJoint` çok katlı binalarda patlıyor); bu nedenle **voksel araziyle doğrudan konuşan kendi C# Burst tabanlı rijit/elastik fizik çözücümüz** en temiz ve en güvenilir yol olarak öne çıkıyor.
