# Alt Ajan Araştırma Raporu: Fiziksel Karakter KCC Motoru, 3B Kumaş Çanta, Madde Kristali ve 3 Katmanlı LoD AI

- **Tarih:** 2026-10-30
- **Alt Ajan ID:** 6635baee-199b-41be-92c7-f037bfb1eeb4
- **Ana Rapor:** [../FAZ1_FAZ2_SORUN_VE_COZUM_MIMARI_RAPORU.md](../FAZ1_FAZ2_SORUN_VE_COZUM_MIMARI_RAPORU.md)

---

# Faz 2 Araştırma Raporu: Karakter, Envanter, Kılıf Sistemi ve Yaratık YZ

## 0. Kodda gördüklerim (salt okunur inceleme)
- `Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs` (577 satır) eski `Input.GetAxis/GetKeyDown` API'sini kullanıyor. Hareket **kapsül çarpışmasız**: karakter `Physics.Raycast` ile zemin yüksekliğini takip ediyor ve pozisyonu doğrudan `transform.position`'a yazıyor (satır 528-541). Bu yüzden duvar, tavan ve tünel kontrolü yok, mantle da yok. Slide fizik değil, sabit hızlı bir zamanlayıcı (`_slideTimer=0.55`, `SprintSpeed*1.35`). `OnGUI` her karede `new GUIStyle` oluşturuyor, bu da GC ayırımı demek.
- `PhysicalCharacterControllerSystem` ve `Creature21BehaviorSystem` aslında ECS sistemi değil, düz `sealed class`. Sabitler de kutsal kitaptan sapmış: sprint 6.8 (olması gereken 7.0), çömelme 2.2/1.25 m (olması gereken 2.0/0.90), prone 1.1/0.65 m (olması gereken 1.0/0.45).
- Manifest'te `com.unity.charactercontroller` ve `com.unity.ai.navigation` **yok**. `netcode 6.6`, `physics 6.6` ve `animation.rigging 6.6` var. Chunk'lar PhysX `MeshCollider` kullanıyor.

## 1. Karakter kontrolcüsü

| Seçenek | Durum ve lisans | Remesh edilen voksel MeshCollider üzerinde |
|---|---|---|
| **DOTS Character Controller** (`com.unity.charactercontroller`) | Unity 6 döneminde 1.x sürümleri var. Örnekler `Unity-Technologies/CharacterControllerSamples` reposunda, lisans **Unity Companion License**. Platformer örneğinde Swim, Climb, LedgeGrab, Crouch state'leri ve `MaxStepHeight`, ground snapping hazır geliyor. | Terrain'in **Unity Physics** collider'ı olarak da olması gerekir. `MeshCollider.Create` chunk başına yaklaşık 5-20 ms sürüyor (Burst job içinde çalışabilir) ve BlobAsset'i elle dispose etmek gerekiyor. PhysX ile birlikte kullanılırsa her chunk **iki kez cook edilir**. |
| **KCC (Philippe St-Amand)** | Asset Store'da **ücretsiz**, ama açık kaynak değil (Asset Store EULA). Özel repoya konabilir, herkese açık dağıtılamaz. | PhysX sweep tabanlı collide-and-slide ile mesh dikişlerine takılmıyor. Çok iyi bir referans. |
| Built-in `CharacterController` | Unity 6'da var. `stepOffset`, `slopeLimit` ve `height/center` değiştirilebiliyor. | Prototip için yeterli. Ama dikişlere takılıyor, ground snap ve sürtünmeli slide yok. |
| Rigidbody (dynamic) | — | Eğimde kayıyor ve ince ayarı zor, önermiyorum. |

**Önerim:** Oyuncu **hibrit GameObject** olsun ve PhysX sorgularıyla (`CapsuleCast`, `Physics.ComputePenetration`, `CheckCapsule`) kendi **kinematik collide-and-slide motorumuzu** yazalım. Mantık KCC'deki gibi: 3-4 iterasyon, yüzey normaline projeksiyon, ground snap için 0.3 m aşağı sweep, adım için yukarı, ileri ve aşağı sweep. Gerekçeler:
- Terrain zaten PhysX'te.
- Yaratıklar fizik collider'ına değil, voksel/NavSpan verisine sorgu atacak.
- Böylece terrain tek bir fizik dünyasında kalır.

**Uyarı:** Netcode for Entities ile tahminli (predicted) çok oyunculu oyun Faz 2'de hedefse DOTS CC tek mantıklı yol olur. O durumda terrain'i Unity Physics'e taşımak gerekir. Bu kararı kullanıcıya sorun.

**Remesh altında düşmeyi önlemek için:**
1. `Physics.BakeMesh(meshId, convex:false, cookingOptions)` çağrısını bir job içinde yapın, ardından ana thread'de `sharedMesh` atayın. Cooking seçenekleri aynı olduğu sürece yeniden cook edilmez.
2. Yeni collider hazır olana kadar eskisini tutun ve atomik olarak değiştirin.
3. Değişimden sonra bir `ComputePenetration` depenetrasyon geçişi yapın.
4. Yedek olarak voksel verisinden yüzey yüksekliğini sorgulayın.

**Duruş, slide, zıplama formülleri:**
- **Çömelme/prone:** kapsül 1.80 → 0.90 → 0.45 m, merkez yükseklik farkının yarısı kadar aşağı (ayak sabit kalır). Ayağa kalkmadan önce `CheckCapsule(hedefYükseklik − skin)` ile kontrol edin. Doluysa kalkmayın.
- **Prone notu:** 0.45 m yüksekliğe dikey kapsül sığarsa yarıçap ≤0.225 olur. Kafanın duvara girmemesi için ayrıca ileri ve geri bir `SphereCast` ekleyin.
- **Slide:** düz zeminde 8.2 m/s ile 3.66 m kayma için a = v²/2d = 67.24/7.32 ≈ **9.19 m/s²**, süre ≈ 0.89 s. Eğimde a = g(μ·cosθ − sinθ), μ = yüzey başına (düz zeminde μ ≈ 0.937·k). `sinθ > μcosθ` olduğunda karakter hızlanır.
- **Zıplama 1.2 m:** v₀ = √(2gh) = **4.85 m/s**.
- **Stamina:** 0'da kilitlenir, **≥%15**'te açılır (histerezis).

**Ledge ve mantle (Mirror's Edge / Dying Light "L-ray" deseni):**
1. Diz (0.5 m), göğüs (1.2 m) ve baş (1.9 m) yüksekliğinden 3 ileri ray atın. Alt ray çarpıp üst ray boşsa ledge adayı var demektir.
2. `hit.point + forward*(r+0.1) + up*2.4` noktasından aşağı ray atın. Bu üst yüzeyi bulur. Normal açısı <35° olmalı.
3. Hedefte ayakta (veya çömelik) kapsül için `CheckCapsule` ile boşluk kontrolü yapın.
4. Yükseklik h ≤ 2.2 ise mantle başlar: motoru kinematik moda alın (çarpışma kapalı) ve cubic Bézier kullanın. P0 = başlangıç, P1 = P0 + up·0.9h, P2 = P3 − fwd·0.3 + up·0.2, P3 = hedef. Süre 0.35 + 0.2h saniye.
5. Vault eşiği h < 1.0 m.

Kaynaklar: https://gamedev.stackexchange.com (ledge detection), weaverdev.io parkour yazısı.

**Diğer hareket state'leri:**
- **Yüzme:** su derinliği = (16-bit akışkan voksel yüzeyi − ayak y). 1.3 m'nin üstünde Swim state'ine geçin: yerçekimi ×0.1, kaldırma kuvveti, sürükleme. Baş su altındaysa O₂ tüketimi başlar.
- **Tırmanma (merdiven/sarmaşık):** trigger hacmi + yüzey normaline kilitlenmiş 2B hareket.
- **Dodge:** 0.25 s boyunca sabit impuls ve i-frame bayrağı.
- **Kamera:** F5 ile FPS/TPS geçişi, TPS'de spring-arm `SphereCast`.

**O₂ ve termal:** Flood fill'i Burst `IJob` yapın: sabit kapasiteli `NativeQueue`, 512 voksel sınırı, gökyüzü ışığı > 0 olunca erken çıkış. Sadece voksel değiştiğinde veya 2-4 Hz'de çalıştırıp sonucu cache'leyin. Termal için Newton soğuması kullanın, kararlı kesin çözümü şu: `T += (T_eq − T)(1 − e^(−dt/τ))`. Burada T_eq = ortam + kıyafet yalıtımı + metabolizma (koşarken artar).

## 2. Diegetik envanter matı (The Forest / Sons of the Forest)
- **Endnight'ın yaklaşımı (topluluk analizleri, resmi GDC konuşması bulamadım):** her eşya türünün mat üzerinde **elle yerleştirilmiş sabit bir yuvası** var. Stack'ler o yuvada görsel kopyalar olarak yığılıyor (gösterim için bir üst sınır var). SotF buna sırt çantası dışında hızlı seçim slotları ekledi. Kaynak: Reddit ve Steam analizleri.
- **Bizim uygulama:**
  1. **Yerleştirme:** `I`'ya basınca oyuncunun 0.9 m önüne zemin normaliyle hizalanmış bir ray atın ve `CheckBox(1.4×0.3×1.0)` ile yer kontrolü yapın. Yer yoksa geri çekilmiş bir pozisyona düşün.
  2. **Bölgeler:** mat local uzayında 6 bölge (3×2 hücre, her biri 0.467×0.5 m).
  3. **Yığın düzeni:**
     - **Piramit:** k. katmanda (L−k)² eşya var. n eşya için katman yüksekliği analitik hesaplanır.
     - **Kule:** i. eşya y = i·h konumunda durur.
  4. **Matrisleri önbellekleyin:** matrisleri yalnızca **envanter değişince (dirty flag)** `NativeArray<Matrix4x4>` içine yazın. `RenderParams` önbellekli olsun ve mesh türü başına bir `Graphics.RenderMeshInstanced` çağrısı yapın. Böylece GC 0 B, CPU < 0.04 ms hedefine ulaşılabilir. `RenderMeshIndirect` sadece GPU culling gerekirse.
  5. **Seçim:** instance başına collider kullanmayın. Kamera ışınını `Plane.Raycast` ile mat düzlemine atın, local UV'den bölge ve yuvayı analitik ters hesapla bulun, sonra stack AABB'sine dar test yapın.
  6. **Kamera:** açılırken yaklaşık 0.3 s içinde mat merkezine ~60° pitch ile blend edin.
  7. **UI Toolkit overlay:** tooltip konumu için `RuntimePanelUtils.CameraTransformWorldToPanel`.
  8. **Hareket:** hız ×0.30.
  9. **Kuşanma:** 3-4 s süren "Channel" state'i. `OnDamaged` olayı gelirse iptal.
- **Sandık:** kapak bir menteşe açısı tween'i olsun. CAS kilidi için `Interlocked.CompareExchange(ref long, (ver+1)<<16|owner, expected)` kullanın. Sürüm uyuşmazsa işlem reddedilir ve dupe olmaz.

## 3. Kılıf / silah değiştirme
- **State makinesi:** `Empty → Drawing(slot: kalça 0.6 s / sırt 1.3 s) → Equipped → Holstering → Empty`. Switch (1.8 s) = Holster + Draw ardışık. Hasar alındığında veya sprint başladığında kuyruk temizlenir.
- **Animation Rigging:** silahın kılıf soketi ile el soketi arasında geçişi için **MultiParentConstraint** ağırlık blend'i kullanın. Böylece reparent kaynaklı "pop" olmaz. Diğer el kabzayı **TwoBoneIK** ile tutar.
- **Zamanlama:** sıfır elle animasyon kuralı nedeniyle zamanlamayı Animator event'leri yerine kod tarafındaki zamanlayıcılar sürsün. Ağırlıklar eğri ile değişsin, "kavrama anı" yaklaşık %55'te.
- **G ile hızlı bırakma:** anlık olsun. Eldeki eşya dünyada pickup entity'si olarak doğar (küçük bir impulsla) ve aktif channel iptal edilir.

## 4. Ölçekli yaratık YZ
- **NavMesh:** yalnızca runtime'da `NavMeshBuilder.UpdateNavMeshDataAsync` ile chunk başına async build mümkün. Ama `NavMeshAgent` GameObject tabanlı ve 3B mağara/kemer yapılarında maliyetli. Paket de kurulu değil. **Önermiyorum.**
- **NavSpan = Recast compact span'ın sıkıştırılmış hali** (`rcCompactSpan y/h/con`). Sütun başına 4 byte öneriyorum:
  - u8 zemin y (chunk-local)
  - u8 boşluk (0.25 m adım)
  - u8 komşu bağlantı bitleri (4 yön × 2 bit tırmanma sınıfı)
  - u8 bayraklar/maliyet (su, eğim, kırılabilir)
  - **Sınırlama:** tek span çok katlı mağaraları temsil etmez. Mağara ringlerinde sütun başına span listesi gerekir. Kaynak: https://recastnav.com
- **Katmanlar:**
  - **T0 (0-128 m):** voksel A*. Kırılabilir duvarın maliyeti `breakCost = (HP / yaratıkDPS) × yürümeHızı` (metre-eşdeğeri) ile hesaplanır. 7DTD de aynı mantıkla A* kullanıyor: blok HP'sini yol maliyetine çeviriyor (açık kapı 0 ≪ ahşap ≪ beton), zombiler "en zayıf halka"ya akın ediyor ve çok uzun labirentlerde duvarı tercih ediyor. Kaynak: 7d2d.net rehberi ve Reddit.
  - **Kule davranışı:** oyuncu zeminden > X m yüksekteyse ve yol maliyeti eşiği aşıyorsa, Structural Collapse'in tributary load DAG'ından `taşınanYük/HP` oranı en yüksek destek vokselini hedefleyin.
  - **T1 (128-512 m):** NavSpan üzerinde paylaşılan **flow field**. Hedef başına (oyuncu, su, sürü hedefi) Dijkstra hesaplanır ve yüzlerce ajan aynı alanı okur.
  - **T2 (>512 m):** sürü düzeyinde analitik simülasyon (merkez, sayı, yön, açlık).
- **Terfi/düşürme:** histerezis kullanın (ör. 120 m'de terfi, 136 m'de düşürme). Yapısal değişiklik yerine `IEnableableComponent` kullanın. Tick'leri kademelendirin: T0 30 Hz, T1 5 Hz, T2 0.5 Hz.
- **Sürü:** `NativeParallelMultiHashMap` spatial hash + boids (ayrılma, hizalanma, birleşme). Referans: ECS Samples Boids. **Utility AI** (Otla, Kaç, İç, Lideri izle, Saldır) için tepki eğrileri, 2-10 Hz.
- **Deep Rock Galactic:** özel bir nav sistemi kullanıyor ve tüm katı/hava sınırı tırmanılabilir yüzey sayılıyor. Resmi GDC konuşması bulamadım, detaylar sınırlı. **Astroneer** ve **Valheim:** Valheim heightmap + dinamik NavMesh kullanıyor ve hendek exploit'leri biliniyor. Bizde de kırma maliyeti yoksa aynı sorun çıkar.

## 5. VAT
- `fuqunaga/VatBaker` (MIT), `sugi-cho/Animation-Texture-Baker` (MIT), `maxartz15/VertexAnimation` (DOTS odaklı, Texture2DArray kullanıyor, **lisansı doğrulayın**).
- **Entities Graphics:** `[MaterialProperty("_AnimTime")] struct : IComponentData` tanımlayın. Shader Graph'ta property'yi "Hybrid Per Instance" yapın. Elle yazılan HLSL'de `UNITY_DOTS_INSTANCING_START` bloğu gerekir. **StratocraftUberShader'ın DOTS instancing desteği olmalı.**
- **VRAM uyarısı (<16 MB):** 800 vertex × 240 kare × RGBAHalf ≈ pozisyon için 1.5 MB, normal için +1.5 MB. 21 yaratıkta bütçe aşılır. **Bone Animation Texture öneriyorum:** 20 kemik × 3 texel × 240 kare × 8 B ≈ 115 KB.
- **Çok bacaklı yaratıklar:** Burst job içinde analitik two-bone IK + adım tetikleyici (gövde-hedef mesafesi > eşik olunca, çapraz faz). Referans: `PhilS94/Unity-Procedural-IK-Wall-Walking-Spider` (lisansı doğrulayın).

## 6. Açık kaynak referanslar
- `Unity-Technologies/CharacterControllerSamples`: Unity Companion License
- `Unity-Technologies/EntityComponentSystemSamples` (Boids): Companion License, doğrulayın
- `recastnavigation/recastnavigation`: zlib
- `Antypodish/Unity_DOTS_NodePathFinding`: MIT
- `kingstone426/NativeFlowField`: MIT olarak raporlandı, **doğrulanmadı**
- `fuqunaga/VatBaker`: MIT
- `sugi-cho/Animation-Texture-Baker`: MIT
- Unity Starter Assets (built-in CC + Input System referansı): Companion License
- KCC: Asset Store EULA, ücretsiz

Arama motoru yönlendirme linkleri döndürdü. Yukarıdaki GitHub yolları elle doğrulanmalı.

## 7. Öneri: Yeniden yaz (strangler deseni), yamalama değil
Mevcut controller'da kapsül çarpışması yok, input eski API ile ve kamera, savaş, voksel düzenleme ve OnGUI aynı sınıfta karışık. Yamalamak daha maliyetli olur. **Korunacaklar:** chunk collider registry'si, voksel düzenleme fonksiyonları ve saf C# matematik sınıfları (sabitleri kutsal kitaba göre düzelterek).

**Mimari:**
- **GameObject:** `PlayerInputReader` (Input System action'ları kodla), `KinematicMotor`, `MovementStateMachine` (Walk/Sprint/Crouch/Prone/Slide/Air/Mantle/Swim/Climb/Dodge), `CameraRig`, `HolsterController` (Rigging), `InventoryMatRenderer`.
- **Saf C# (EditMode test edilebilir):** `StaminaModel`, `SlideModel`, `ThermalModel`, `MantleSolver`.
- **ECS (`ISystem`, Burst):** O₂ flood fill, yaratık LoD/YZ/flow field, VAT zamanı. Köprü: MonoBehaviour her kare `PlayerStateSingleton` yazar, ECS'ten gelen hasar olaylarını okur.

**Uygulama sırası:**
1. Input + motor + kamera
2. Duruşlar + tavan kontrolü
3. Slide + stamina
4. Mantle
5. Yüzme/tırmanma
6. O₂/termal
7. Envanter matı
8. Kılıflar + sandık CAS
9. NavSpan üretimi
10. Yaratık T0/T1/T2 + sürü
11. VAT/BAT

**Test checklist'i:**
- **EditMode:**
  - slide 3.66±0.05 m / 0.89 s
  - zıplama tepe noktası 1.20±0.02 m
  - stamina kilidi 0'da, açılış %15'te
  - Bézier uç noktaları
  - termal kararlılık (dt=0.5)
  - CAS paralel 10k deneme, 0 dupe
- **PlayMode:**
  - düz zeminde 5 s sonunda 20±0.5 m yol
  - 1.0 m tavan altında ayağa kalkılamaz
  - 0.5 m tüneli prone ile geçebilir
  - 2.2 m mantle başarılı, 2.3 m başarısız
  - ayak altında 100 kez remesh, 0 kez düşme
  - mat açıkken hız 1.2 m/s
  - hasar kuşanmayı iptal eder
  - flood fill ≤512 voksel ve gökyüzünde erken çıkış
- **Performans:**
  - motor < 0.1 ms
  - mat < 0.04 ms
  - kare başına GC 0 B
  - 21 tür × N yaratıkta T1 ≤ 0.5 ms
  - VRAM < 16 MB

Ardından quality-gate çalıştırılmalı.
</SYSTEM_MESSAGE>
