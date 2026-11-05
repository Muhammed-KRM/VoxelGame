# PERFORMANS RAPORU 2: MEVCUT CANLI/AI, SÜRÜ (HORDE), KARAKTER FİZİĞİ, AĞ (NETCODE) VE UI/GC ANALİZİ

> **Rapor Kapsamı:** `Assets/_Stratocraft/Runtime/CreaturesAI/`, `Assets/_Stratocraft/Runtime/Character/`, `Assets/_Stratocraft/Runtime/Netcode/`, `Assets/_Stratocraft/Runtime/Inventory/`, `Assets/_Stratocraft/Runtime/Core/` ve `Assets/_Stratocraft/Runtime/Telemetry/` altındaki tüm çalışma zamanı sınıflarının satır satır incelenmesi; Anayasa (`docs/STRATOCRAFT_ANAYASA_VE_MIMARI_DOKUMAN.md`) ve `docs/faz2-gorev-Plani.md` hedefleriyle karşılaştırılması.

---

## 1. Anayasa Hedefleri vs. Mevcut Kodun Gerçek Uygulama Durumu (Özet Tablo)

| Alt Sistem / Optimizasyon Katmanı | Anayasa & Faz 2 Mimari Hedefi | Mevcut Kodda Dosya / Sınıf | Gerçek Durum (Aktif mi?) | Darboğaz / Eksik Kısım |
| :--- | :--- | :--- | :--- | :--- |
| **Hibrit AI LOD (Excel Spreadsheet Simülasyonu)** | `> 96m` uzaktaki canlıların 3B `GameObject` ve fizikleri kapatılır (`%0 GPU/CPU`), 64-baytlık deterministik kayıtta $P(t) = P_0 + \vec{v}(t - t_0)$ ile izlenir. | `OffScreenEntityRecord.cs`, `HybridAiLodPromotionSystem.cs`, `Stage1CreatureSimulationBridge.cs` (`L314-L525`) | **AKTİF (Çalışıyor)** | Sadece sahnedeki ~11 canlı için çalışıyor; binlerce canlılık sürü (horde) için ECS / `NativeArray` tabanlı toplu güncelleme henüz bağlı değil. |
| **Zaman Dilimli (Time-Sliced) 3D JPS+ A\* Yol Bulma** | Yol bulma sorguları kare başına bütçelenir (`Frame Budget`), tüm sürü aynı karede A\* çalıştırmaz. | `TimeSlicedJumpPointAStar.cs`, `Stage1CreatureSimulationBridge.cs` (`L106`, `L925-L980`) | **KISMEN AKTİF** | Kare başına en fazla 1 A\* sorgusu sınırı (`_lastAStarFrame`) aktif; ancak her sorguda `16x16x64` (`16.384` hücre) voksel Ana İş Parçacığında (Main Thread) `_navChunkBuffer` dizisine kopyalanıyor ve JPS sıçrama tabloları yerine 26-komşulu standart A\* çalışıyor. |
| **Dikey Sütun Özeti (`NavSpanColumnSummary`)** | Her X-Z sütunu için yürünebilir Y aralıklarını önbelleğe alıp 3B voksel taramasını $O(1)$'e indirmek. | `NavSpanColumnSummary.cs` | **KAĞIT ÜSTÜNDE (Devre Dışı)** | `readonly struct` tanımlı ve birim testleri var; fakat `Stage1CreatureSimulationBridge` içinde hiç çağrılmıyor. |
| **Sürü (Boids) Ayırma ve Kuşatma (Flanking)** | Yüzlerce canlının birbirinin içine girmemesi ve oyuncuyu 360° sarması. | `Stage1CreatureSimulationBridge.cs` (`ComputeBoidsSeparationAndFlankDirection`, `L983-L1023`) | **AKTİF AMA $O(N^2)$** | Mekansal Hash Izgarası (`Spatial Hash Grid`) yok. Her canlı tüm `_creatures` listesini doğrusal tarıyor ($O(N^2)$). |
| **GPU Vertex Animation Texture (VAT) & Tek-Mesh Çizim** | İskelet (`Animator` / `SkinnedMeshRenderer`) kullanmadan tüm canlı animasyonlarını GPU'da `Half4` VAT dokusuyla (`%0 CPU`) oynatmak ve `GPU Instancing` ile tek seferde çizmek. | `StratocraftUberShader.shader`, `Stage1CreatureSimulationBridge.cs` (`EnsureCreatureArticulatedLimbs`, `L1028-L1150`) | **YARI AKTİF (Ciddi CPU Yükü Var)** | `StratocraftUberShader` içinde VAT ve `MaterialPropertyBlock` desteği var; **fakat** `EnsureCreatureArticulatedLimbs` her canlıya **12–16 adet alt `GameObject` (`Pivot_Head`, `Pivot_FL_Leg` vb.)** ekleyip her karede Ana İş Parçacığında `Quaternion.Euler` ile `Transform` döndürüyor! |
| **240 Hz Kamera-Debriyaj (`Camera-Clutch`) + `1-Euro Filter`** | Fare titremesini sönümleyen `1€` adaptif filtre ve serbest salınım koridoru. | `OneEuroFilter3D.cs`, `CameraClutchController.cs`, `Stage1CombatFeedbackAndArenaBridge.cs` | **AKTİF (%100 Çalışıyor)** | Tamamen `struct` tabanlı, `0 B` GC tahsisiyle çok hızlı çalışıyor. |
| **Analitik Termal & Kapalı Alan Oksijen Çözücü** | Üstel analitik ısı denklemi ve zaman-bütçeli (`0.30 sn`) 3B Flood-Fill oksijen kontrolü. | `ThermalAndBiologySystem.cs`, `EnclosedOxygenFloodFillSystem.cs`, `Stage1InteractivePlayController.cs` | **AKTİF (%100 Çalışıyor)** | Oksijen BFS taraması `0.30 sn` aralığa ve `600` hücreye sınırlandırılmış durumda; CPU yükü ihmal edilebilir (`< 0.05 ms`). |
| **60 Hz Otoriter Ağ (Netcode) & Geri Sarma (Lag Comp.)** | `150 ms` sunucu geçmiş halkası, `FixedPointQ20_12` deterministik fizik, `GhostDistanceImportance` ve `XOR + RLE` paket sıkıştırma. | `AuthoritativeCombatTickSystem.cs`, `FixedPointQ20_12.cs`, `GhostDistanceImportanceConfig.cs`, `GhostSnapshotDeltaCompressor.cs` | **VERİ YAPISI HAZIR, SAHNEDE DEVRE DIŞI** | `Packages/manifest.json` içinde `com.unity.netcode 6.6.0` kurulu ve `ServerWorld`/`ClientWorld` oluşuyor; ancak canlılar ve oyuncular henüz `GhostAuthoringComponent` / ECS `ISystem` üzerinden senkronize edilmiyor. |
| **Üç Katmanlı SQLite WAL Telemetri** | Oyun içi hataları ana iş parçacığını bloklamadan arka planda SQLite'a yazmak. | `SqliteTelemetryLogger.cs`, `RingBufferLogQueue.cs`, `LogFingerprintDeduplicator.cs` | **AKTİF (%100 Çalışıyor)** | Kilit-serbest (`Interlocked`) halka tampon + arka plan thread'i ile `0 ms` ana thread maliyeti. |
| **Oyun İçi Arayüz (HUD / Envanter / ESC Menüsü)** | Sıfır GC tahsisi (`0 B/kare`) ile çalışma. | `Stage1InteractivePlayController.OnGUI`, `Stage1VisualHotbarAndInventoryUi.OnGUI`, `Stage1CombatFeedbackAndArenaBridge.OnGUI` | **DARBOĞAZ (Her Kare GC Üretiyor)** | Tüm UI `MonoBehaviour.OnGUI()` (eski IMGUI) ile yazılmış. Her karede onlarca `$"..."` string birleştirmesi yapılıyor ve IMGUI Layout geçişi CPU zamanı harcıyor. |

---

## 2. Mevcut Kodda Kullanılan Algoritmalar, Matematiksel Modeller ve Teknolojiler

### 2.1. Hibrit AI LOD ("Excel Tablosu" Dış-Ekran Simülasyonu)
* **Dosyalar:** [`OffScreenEntityRecord.cs`](../Assets/_Stratocraft/Runtime/CreaturesAI/OffScreenEntityRecord.cs), [`HybridAiLodPromotionSystem.cs`](../Assets/_Stratocraft/Runtime/CreaturesAI/HybridAiLodPromotionSystem.cs), [`Stage1CreatureSimulationBridge.cs`](../Assets/_Stratocraft/Runtime/CreaturesAI/Stage1CreatureSimulationBridge.cs) (`L314-L525`).
* **Nasıl Çalışır:**
  * Her canlı için `64 bayt` büyüklüğünde (`[StructLayout(LayoutKind.Explicit, Size = 64)]`) bir [`OffScreenEntityRecord`](../Assets/_Stratocraft/Runtime/CreaturesAI/OffScreenEntityRecord.cs#L11-L41) tutulur.
  * Canlı oyuncudan `PromoteOutDistance = 96f` metreden uzağa çıktığında (`Stage1CreatureSimulationBridge.cs#L397-L407`), `actor.Transform.gameObject.SetActive(false)` çağrılır. Böylece Unity'nin `Transform`, `MeshRenderer` ve `BoxCollider` güncellemeleri tamamen sıfırlanır.
  * Canlı uzaktayken her karede `Update()` döngüsü çalıştırmak yerine **Analitik Doğrusal Hareket Denklemi** kullanılır (`OffScreenEntityRecord.cs#L46-L56`):
    $$\vec{P}(t) = \vec{P}_0 + \vec{v} \cdot \Delta t$$
  * Oyuncu canlıya `PromoteInDistance = 88f` mesafeye yaklaştığında veya kamera görüş konisi (`Frustum Dot >= 0.42f` ve `< 125m`) içine girdiğinde (`HybridAiLodPromotionSystem.EvaluateLodState`), canlının o anki analitik X-Z konumu hesaplanır, `SampleTerrainTopY` ile yüzey yüksekliğine oturtulur ve `gameObject.SetActive(true)` ile "Just-In-Time" (tam zamanında) 3B dünyaya terfi ettirilir.

### 2.2. Zaman Dilimli (Time-Sliced) 3D A\* ve Voxel DDA Görüş Hattı
* **Dosyalar:** [`TimeSlicedJumpPointAStar.cs`](../Assets/_Stratocraft/Runtime/CreaturesAI/TimeSlicedJumpPointAStar.cs), [`Stage1CreatureSimulationBridge.cs`](../Assets/_Stratocraft/Runtime/CreaturesAI/Stage1CreatureSimulationBridge.cs) (`L872-L980`).
* **Nasıl Çalışır:**
  * Canlı ile hedef arasındaki doğrudan görüş hattı `HasDirectVoxelLineOfSight` (`Stage1CreatureSimulationBridge.cs#L872-L904`) ile kontrol edilir.
  * Doğrudan yol kapalıysa `_lastAStarFrame != Time.frameCount` kontrolüyle (`L907`) **kare başına en fazla 1 adet** 3D A\* araması başlatılır.
  * [`TimeSlicedJumpPointAStar`](../Assets/_Stratocraft/Runtime/CreaturesAI/TimeSlicedJumpPointAStar.cs#L8-L320) sınıfı, `MinHeap` öncelik kuyruğu, Oktil (Octile) 3B sezgisel maliyet fonksiyonu ve 26 yönlü komşuluk (düz yürüme, $\pm 1$ basamak çıkma/inme ve $2\times 2$ kuadratik sıçrama parabolü) kullanarak `maxExpandedNodes = 192` düğüm bütçesiyle engel etrafından dolaşan ilk sapma noktasını (`FirstStepPosition`) bulur.

### 2.3. 240 Hz Kamera-Debriyaj (`Camera-Clutch`), `1€` Jitter Filtresi ve 15 Parçalı Zırh
* **Dosyalar:** [`OneEuroFilter3D.cs`](../Assets/_Stratocraft/Runtime/Character/OneEuroFilter3D.cs), [`CameraClutchController.cs`](../Assets/_Stratocraft/Runtime/Character/CameraClutchController.cs), [`ArmorPaperdollLoadout.cs`](../Assets/_Stratocraft/Runtime/Character/ArmorPaperdollLoadout.cs), [`Stage1CombatFeedbackAndArenaBridge.cs`](../Assets/_Stratocraft/Runtime/Character/Stage1CombatFeedbackAndArenaBridge.cs).
* **Nasıl Çalışır:**
  * [`OneEuroFilter3D`](../Assets/_Stratocraft/Runtime/Character/OneEuroFilter3D.cs#L11-L97), Géry Casiez'in adaptif alçak geçiren filtresini (`MinCutoff`, `Beta`, `DerivativeCutoff`) gerçek zamanlı uygular: düşük hızda titremeyi keser, yüksek hızda (`Beta`) gecikmeyi (lag) sıfıra indirir.
  * [`ArmorPaperdollLoadout`](../Assets/_Stratocraft/Runtime/Character/ArmorPaperdollLoadout.cs) 8 uzuv $\times$ 3 katman (Kumaş, Zincir, Plaka) + Sırt Çantası olmak üzere 15 parçayı sabit dizi/struct üzerinde değerlendirir, eklem tork cezasını ve bölgesel ısı yalıtımını $O(1)$ sürede hesaplar.

---

## 3. Canlı, Sürü, Ağ ve UI Katmanlarında Kasmaya Sebep Olan Gerçek Darboğazlar

### Darboğaz #1: `EnsureCreatureArticulatedLimbs` — Canlı Başına 15 Alt `GameObject` ve Ana Thread `Transform` Hierarşisi
* **Konum:** [`Stage1CreatureSimulationBridge.cs`](../Assets/_Stratocraft/Runtime/CreaturesAI/Stage1CreatureSimulationBridge.cs#L1028-L1200) (`EnsureCreatureArticulatedLimbs` ve `AnimateCreatureArticulatedLimbs`).
* **Sorun:**
  * Anayasa'da canlıların tek bir birleşik `Mesh` olup `StratocraftUberShader` içindeki GPU Vertex Animation Texture (VAT) ile `%0 CPU` maliyetiyle hareket etmesi hedeflenmişti.
  * Ancak görsel eklem hareketi sağlamak için `EnsureCreatureArticulatedLimbs` metodu her canlıya **12 ila 16 adet çocuk `GameObject` (`Pivot_Head`, `Pivot_FL_Leg`, `Limb_Mesh` vb.)** ve her birine ayrı `MeshFilter` + `MeshRenderer` ekliyor!
  * Sonuç: Sahnede 11 canlı varken bile `11 x 15 = 165` adet ek `Transform` ve `MeshRenderer` oluşuyor. Eğer 300 kişilik bir sürü (horde) saldırırsa **`4.500` adet `Transform`** her karede Ana İş Parçacığında `Quaternion.Euler` ile güncellenecek ve Unity'nin `Transform` kirli bayrak (dirty flag) yayılımı CPU'yu tamamen kilitleyecektir.

### Darboğaz #2: Her A\* Sorgusunda `16.384` Vokselin Ana İş Parçacığında Kopyalanması (`PrepareNavChunkBuffer`)
* **Konum:** [`Stage1CreatureSimulationBridge.cs`](../Assets/_Stratocraft/Runtime/CreaturesAI/Stage1CreatureSimulationBridge.cs#L951-L980).
* **Sorun:**
  * Bir canlı engelle karşılaşıp `TryComputeAStarDetourDirection` çağırdığında, `PrepareNavChunkBuffer` metodu `16 x 16 x 64 = 16.384` hücrelik üçlü `for` döngüsünü Ana İş Parçacığında çalıştırıp `_voxelController.GetVoxel(wx, wy, wz)` ile her hücreyi tek tek sorgulayarak `_navChunkBuffer` dizisine kopyalıyor.
  * Üstelik her canlı kendi başına A\* çalıştırıyor; **oysa 500 canlı aynı oyuncuya doğru koşarken 500 kez A\* çalıştırmak yerine oyuncudan dışarıya yayılan tek bir 3B Akış Alanı (Flow Field / Dijkstra Grid) hesaplanmalıdır.**

### Darboğaz #3: $O(N^2)$ Boids Sürü Döngüsü ve Mekansal Hash (`Spatial Hash Grid`) Eksikliği
* **Konum:** [`Stage1CreatureSimulationBridge.cs`](../Assets/_Stratocraft/Runtime/CreaturesAI/Stage1CreatureSimulationBridge.cs#L983-L1023) (`ComputeBoidsSeparationAndFlankDirection`).
* **Sorun:**
  * Her aktif canlı, diğer tüm canlılarla arasındaki mesafeyi kontrol etmek için `for (int i = 0; i < _creatures.Count; i++)` döngüsüne giriyor.
  * 11 canlı için $11^2 = 121$ kontrol sorun yaratmazken, *7 Days to Die* veya *Deep Rock Galactic* tarzı 500 canlılık bir kuşatmada $500^2 = 250.000$ kontrol her karede Ana İş Parçacığında çalışır.

### Darboğaz #4: `Unity.NetCode` (ECS) Dünyalarının Boşta Çalışması ve `Server Tick Batching` Uyarısı
* **Konum:** `Packages/manifest.json` (`com.unity.netcode: 6.6.0`, `com.unity.entities: 6.6.0`) ve [`AuthoritativeCombatTickSystem.cs`](../Assets/_Stratocraft/Runtime/Netcode/AuthoritativeCombatTickSystem.cs).
* **Sorun:**
  * Unity 6 açıldığında `com.unity.netcode` paketi otomatik olarak `ServerWorld` ve `ClientWorld` ECS dünyalarını başlatıyor (Hiyerarşi penceresinde görüldüğü gibi).
  * Ana iş parçacığında voksel örme veya sahne yükleme sırasında kare süresi `16.6 ms` üstüne çıktığında, `ServerWorld` kendi sabit `SimulationTickRate` hızını yakalamak için bir karede birden fazla tick çalıştırıyor ve konsola `Server Tick Batching has occurred due to the server falling behind its desired SimulationTickRate` uyarısını basıyor.
  * Aynı zamanda gerçek ağ sınıflarımız (`AuthoritativeCombatTickSystem`, `GhostDistanceImportanceConfig`, `GhostSnapshotDeltaCompressor`) henüz ECS `ISystem` ve `Burst` ile bağlanmadığı için çok oyunculu ölçekleme altyapısı atıl duruyor.

### Darboğaz #5: `MonoBehaviour.OnGUI()` (IMGUI) ve Her Karede String (`$"..."`) GC Tahsisleri
* **Konum:**
  * [`Stage1InteractivePlayController.cs`](../Assets/_Stratocraft/Runtime/Core/Stage1InteractivePlayController.cs#L1553-L1638) (`OnGUI`)
  * [`Stage1VisualHotbarAndInventoryUi.cs`](../Assets/_Stratocraft/Runtime/Core/Stage1VisualHotbarAndInventoryUi.cs#L430-L820) (`OnGUI`)
  * [`Stage1CombatFeedbackAndArenaBridge.cs`](../Assets/_Stratocraft/Runtime/Character/Stage1CombatFeedbackAndArenaBridge.cs#L825-L980) (`OnGUI`)
  * [`Stage1EscapeMenuAndSettingsController.cs`](../Assets/_Stratocraft/Runtime/Core/Stage1EscapeMenuAndSettingsController.cs#L242-L540) (`OnGUI`)
* **Sorun:**
  * Unity'nin eski `OnGUI()` sistemi her karede hem `EventType.Layout` hem `EventType.Repaint` olmak üzere **en az 2 kez** çalışır.
  * Bu 4 sınıfın `OnGUI()` metotları içinde her karede `$"Konum: X={pos.x:F0}..."`, `$"[{i + 1}] {label}"`, `$"HP:{actor.CurrentHp:F0}/{actor.MaxHp:F0}"` gibi **40'tan fazla yeni `System.String` nesnesi** Managed Heap üzerinde oluşturulup çöpe atılmaktadır.
  * Bu durum Anayasa'daki **`0 B/kare GC Alloc`** kuralını ihlal etmekte ve belirli aralıklarla C# Çöp Toplayıcısının (Garbage Collector) devreye girerek anlık takılmalara (micro-stutter) yol açmasına neden olmaktadır.

---

## 4. Özet Sonuç: Canlı, Sürü ve Online Sistemlerde Ne Yapılmalı?

1. **Tek-Mesh GPU VAT + `Graphics.RenderMeshInstanced` / `BatchRendererGroup`:** `EnsureCreatureArticulatedLimbs` içindeki 15 çocuk `GameObject` hiyerarşisi kaldırılmalı; uzuv kimliği (`limbId`) vertex renginin (`Color32.a`) veya UV kanalının içine gömülerek tüm uzuv salınımları **tek bir birleşik Mesh** üzerinde doğrudan `StratocraftUberShader.shader` Vertex Shader'ında (GPU'da) yapılmalıdır.
2. **Paylaşımlı 3B Akış Alanı (`3D Flow Field`) + `NativeParallelMultiHashMap` Spatial Grid:** Sürüdeki 500 canlının her birine ayrı A\* koşturmak yerine, oyuncunun bulunduğu hedeften dışarıya Burst Job içinde saniyede 5 kez yayılan tek bir `3D Dijkstra Flow Field` üretilmeli; Boids çarpışma/ayrılma testi ise `NativeParallelMultiHashMap<int, int>` mekansal hash tablosuyla $O(N^2)$'den $O(N)$'e indirilmelidir.
3. **Sıfır-GC UI:** `OnGUI()` metotlarındaki metinler yalnızca değer değiştiğinde (dirty check) güncellenen önbellekli string'lere veya Unity 6 `UI Toolkit` (`UIDocument`) sistemine taşınmalıdır.
