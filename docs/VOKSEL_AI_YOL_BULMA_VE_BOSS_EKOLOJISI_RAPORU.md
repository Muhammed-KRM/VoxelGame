# Yıkılabilir 3B Voksel Dünyada Canlı AI Yol Bulma (`Voxel A*`), Sürü Ekolojisi ve Boss Evreleri Mimari Raporu

> **Kapsam:** Sektör Analizi (*7 Days to Die*, *Minecraft Baritone*, *Dwarf Fortress*, *Vintage Story*, *Valheim*), Açık Kaynak Voxel A* Algoritmaları, Mevcut Kod Tabanı Kök Neden Tespiti (`Stage1CreatureSimulationBridge.cs`, `DestructibleNavSpanAStar.cs`, `Creature21BehaviorSystem.cs`, `HyperbolicLanchesterCombat.cs`) ve Canlı Sahne Entegrasyon Mimarisi.

---

## 1. Endüstri Analizi ve Açık Kaynak Voksel Yol Bulma Mekanikleri

Yıkılabilir 3B voksel dünyalarda standart statik `NavMesh` yapıları, her blok kırıldığında veya inşa edildiğinde yeniden pişirme (`Re-baking`) darboğazı yarattığı için yetersiz kalır. Sektördeki referans oyunlar ve açık kaynak projeler bu problemi **Blok Kırma Maliyetli Dinamik Voxel A*** ve **Çok Katmanlı Sütun/Span Özetleri (`Height Spans`)** ile çözer:

### 1.1. Referans Oyunlar ve Kuşatma / Yıkım Algoritmaları

| Oyun / Proje | Yıkılabilir Voksel Yol Bulma (`Pathfinding`) Mimarisi | Taşıyıcı Kolon ve Duvar Kırma (`Siege / Anti-Cheese`) Mantığı |
| :--- | :--- | :--- |
| **7 Days to Die** | **Maliyet Ağırlıklı 3B Voxel A\*:** Zombiler oyuncuya düz çizgide yürümez; A* grafında yürüme maliyeti ile blok kırma süresini ($t_{\text{breach}} = \text{HP}_{\text{blok}} / \text{DPS}_{\text{kuşatma}}$) birlikte değerlendirir. Çelik duvarın yanındaki ahşap kapı daha düşük $g(n)$ maliyeti ürettiği için AI doğal olarak zayıf noktaya yönelir. | **Yapısal Bütünlük (`Structural Integrity`) + Rage/Pillar Hedefleme:** Oyuncu merdivensiz yüksek bir sütun/platform (`stilt base`) üzerine çıktığında ve geçerli yürünebilir rota ($h(n) \to 0$) bulunamadığında, zombiler alt kattaki taşıyıcı kolonlara (`Support Pillars`) saldırır. Kolon kırıldığı anda yapısal fizik motoru üstteki tüm platformu çökertir. |
| **Minecraft Baritone** (`cabaletta/baritone`) | **Eylem Tabanlı Dinamik Kenar Maliyetli A\* (`Action-Graph A*`):** Açık kaynaklı `Baritone` ve `mineflayer-pathfinder`, her voksel geçişini bir hareket profiliyle (`MovementTraverse`, `MovementAscend`, `MovementDescend`, `MovementParkour`, `MovementPillar`) modeller. Kenar maliyeti $c(u, v) = t_{\text{walk}} + \text{digCost}(v) + \text{placeCost}(v)$ olarak hesaplanır. | **Admissible Heuristic & Kazma Maliyeti:** Blok sertliği ve araç çarpanına göre `digCost` hesaplanır; kırılması imkânsız bloklara (Bedrock/Obsidian) $\infty$ maliyet atanır. Sezgisel fonksiyon $h(n)$ yalnızca geometrik Octile/Manhattan mesafesini ölçtüğü için ($h(n) \le \text{gerçek maliyet}$) A* optimal en ucuz yıkım/yürüme kombinasyonunu bulur. |
| **Dwarf Fortress** | **Çoklu Z-Katmanlı (`Multi-Z-Level`) Hiyerarşik A\*:** Bağlantılı bileşen önbellekleri (`Connected Component Caching`) ile hangi Z-katmanlarının birbirine merdiven/rampa ile bağlı olduğunu $O(1)$'de kontrol eder; ardından yerel A* çalıştırır. | **Bina Yıkıcılar (`Building Destroyers`):** Kapılar kilitlendiğinde ve açık rota kalmadığında *Troll*, *Minotaur* ve *Forgotten Beast* sınıfı varlıklar `BuildingDestroyer` bayrağıyla kapıları ve yapay duvarları hedef düğüm olarak seçip yıkar. |
| **Vintage Story** | **Blok Katmanı Farkındalıklı Hibrit Grid A\*:** *Temporal Storm* sırasında doğan *Drifter* sürüleri, performans bütçesini korumak için sınırlı düğüm tavanlı (`Node-Capped`) A* kullanır ve kısmi rotalarla (`Partial Path Fallback`) hedefe yaklaşır. | **Seviye Bazlı Blok Kırma:** Yaratıklar oyuncunun saklandığı kapalı alanlardaki toprak, ahşap veya zayıf taş blokları kazma/kırma yeteneğiyle parçalayarak içeri sızar. |
| **Valheim** | **Arazi NavMesh + Yapısal Engel Hedefleme:** Arazi üzerinde standart yol bulma çalışırken, oyuncu yapıları (`Piece`) yol tıkadığında AI "Engel Temizleme (`Obstruction Breach`)" moduna geçer. | **Alan Etkili (`AoE`) Temel Yıkımı:** *Troll* ve kuşatma yaratıkları, oyuncuya ulaşamadığında en yakın yapı elemanına saldırır; Valheim'ın destek mesafesi sistemi alt bloklar kırılınca üst çatıyı otomatik yıkar. |

### 1.2. Açık Kaynak Voxel/Span Yol Bulma Kütüphaneleri
* **`recastnavigation/recastnavigation` (`Recast & Detour`):** 3B geometriden dikey voksel sütunları (`CompactHeightfield` ve `RecastSpan`) çıkararak yürünebilir taban/tavan aralıklarını (`FloorY`, `Headroom`) sıkıştırır. Stratocraft'taki [`NavSpanColumnSummary`](../Assets/_Stratocraft/Voxel/Data/NavSpanColumnSummary.cs) (4-Byte) ve [`NavSpanColumn4Layer`](../Assets/_Stratocraft/Voxel/Data/NavSpanColumn4Layer.cs) (16-Byte, 4 katmanlı mağara/köprü desteği) doğrudan bu mimarinin sıfır-tahsisli (`Zero-GC`) voksel uyarlamasıdır.
* **`cabaletta/baritone`:** Kısmi yol geri dönüşü (`bestPartialNode` / `PathingBehavior`) sayesinde arama bütçesi (ör. `320` düğüm) dolduğunda bile ajanın donup kalmasını önler; hedefe sezgisel olarak en çok yaklaşan düğüme kadar yürütüp bir sonraki dilimde aramayı sürdürür.
* **`darbycostello/Nav3D` (Sparse Voxel Octree - SVO):** Uçan Boss ve yaratıklar (`WingedFlight`) için 3B hacimsel boşluklarda Octree/3D Grid tabanlı A* ve Theta* rotalaması sağlar.

---

## 2. Sürü Ekolojisi (`Boids` + `Hyperbolic Lanchester` + `Morale/Flee`) ve Çok Evreli Boss Mekanikleri

### 2.1. Canlı Sürü Davranışı ve Lanchester Moral Denklem Sistemi
Standart **Craig Reynolds Boids** algoritması (*Separation* $\vec{f}_{\text{sep}}$, *Alignment* $\vec{f}_{\text{ali}}$, *Cohesion* $\vec{f}_{\text{coh}}$) sürülerin akıcı hareket etmesini sağlar ancak tek başına "kör" bir kümeleme üretir; sürü üyeleri ölüme giderken bile birbirine yapışmaya devam eder. Gerçekçi bir ekosistem için **Boids yönlendirme vektörleri**, **Hiperbolik Lanchester Savaş Gücü Oranı** ile dinamik olarak ağırlıklandırılmalıdır:

1. **Yerel Algı ve Lanchester Güç Oranı ($\rho_{\text{local}}$):**
   Yaratık, algı yarıçapı ($R_{\text{sense}}$) içindeki dost sürünün toplam etkin gücünü ($\Phi_{\text{ally}} = \sum \text{HP}_i \cdot \sqrt{\alpha_i}$) ve tehdit/oyuncu gücünü ($\Phi_{\text{threat}} = \text{HP}_p \cdot \sqrt{\alpha_p}$) karşılaştırır:
   $$B_{\text{lanchester}} = \frac{\Phi_{\text{ally}}}{\Phi_{\text{threat}}}$$
2. **Moral ($M \in [0, 1]$) ve Sürü Durum Geçişi:**
   * **Baskın Sürü ($B_{\text{lanchester}} \ge 1.15$ veya Alfa Hayatta):** *Cohesion* ve *Flanking* (yandan kuşatma: `PackAlpha` önden, `Flanker01` ve `Flanker02` $\pm 45^\circ$ kanatlardan) ağırlıkları yüksektir.
   * **Kırılan Moral ve Kaçış ($B_{\text{lanchester}} < 0.45$, Can $< \%25$ veya Sürü Alfası Öldü):** Sürü paniğe kapılır (`Flee` durumu). Bu durumda *Cohesion* (kümeleşme) **sıfırlanır**, *Separation* (dağılma) **2.5x** artırılır ve tehdit merkezinden dışarı doğru güçlü bir kaçış vektörü ($\vec{f}_{\text{flee}} = \text{ normalize}(\vec{p}_{\text{agent}} - \vec{p}_{\text{threat}})$) uygulanır.

### 2.2. Çok Evreli Boss Mekaniği: Phoenix (`Anka Kuşu`) Kor Yumurta (`Ember Egg`) Döngüsü
Kanonik Stratocraft kurallarına ([`STRATOCRAFT_GELISTIRICI_DOKUMANI.md` Bölüm 4.14](../STRATOCRAFT_GELISTIRICI_DOKUMANI.md) ve [`stratocraft-combat-and-catalogs`](../.agents/skills/stratocraft-combat-and-catalogs/SKILL.md)) göre **Phoenix (`Anka Kuşu / Anka Kor Avatar`)** üç evreli bir yaşam döngüsüne sahiptir:
* **Evre 1 — Hava Hakimiyeti ve Kor Saldırısı (`Phase1_AerialAvatar`):** `WingedFlight` yeteneği aktifken yüksek irtifada süzülür, dalış saldırıları yapar. Kanadı kesilirse (`WingLeft` / `WingRight` kopması) `WingedFlight` bayrağı düşer, hızı `%50`'ye iner ve yerde savaşmak zorunda kalır.
* **Evre 2 — Kor Yumurta Evresi (`Phase2_EmberEgg` — `10.0 sn` / `2.500 HP`):**
  Phoenix'in canı `0 HP`'ye düştüğünde (ve henüz yeniden doğmamışsa) **ölmez!** Olduğu zemin yüksekliğine çökerek **`2.500 HP`** cana sahip hareketsiz, kor halinde parlayan bir **Kor Yumurtaya (`Ember Egg`)** dönüşür ve **`10.0 saniyelik`** geri sayım sayacı (`EmberEggTimer`) başlar.
  * Bu `10 saniye` boyunca oyuncu yumurtaya fiziksel/yakın dövüş hasarı vererek `2.500 HP`'yi sıfırlarsa, Kor Yumurta parçalanır ve Boss **kalıcı olarak ölür (`Dead`)**.
* **Evre 3 — Küllerinden Yeniden Doğuş (`Phase3_RebornEnraged` — `%60 HP` / `1.5x Hasar`):**
  Eğer oyuncu `10.0 saniye` içinde `2.500 HP`'lik Kor Yumurtayı kıramazsa, yumurta patlayarak açılır; Phoenix **maksimum canının `%60`'ı** (kanonik tam Boss ölçeğinde `6.000 HP` / sahne ölçeğinde `0.60 * MaxHp`), **`1.5x` saldırı hasarı çarpanı (`DamageMultiplier = 1.5f`)** ve onarılmış uçuş yeteneği ile yeniden doğar!

---

## 3. Mevcut Kod Tabanı İncelemesi ve Kök Neden Tespiti (`Root-Cause Analysis`)

Mevcut kod tabanındaki ilgili dosyalar incelendiğinde, arka plan algoritmaları ile canlı sahne köprüsü ([`Stage1CreatureSimulationBridge.cs`](../Assets/_Stratocraft/Character/Stage1CreatureSimulationBridge.cs)) arasındaki kopukluğun **4 somut kök nedeni** tespit edilmiştir:

### Kök Neden 1: `DestructibleNavSpanAStar.FindPath` Metodunun Canlı Sahnede Hiç Çağrılmaması (`flatDir` Kör Yürüyüş)
* **Arka Planda Ne Var?** [`DestructibleNavSpanAStar.cs` (satır 65–209)](../Assets/_Stratocraft/CreaturesAI/Pathfinding/DestructibleNavSpanAStar.cs#L65-L209) içinde `MaxExpandedNodes = 320`, `HeuristicWeightEpsilon = 1.25f`, blok kırma süresi hesabı (`breachTime = blockHp / siegeDps`), Boss zindan koruma kuralı (`isSanctumBoss && OriginState == DogalKutle`) ve `bestPartial` geri dönüşü eksiksiz kodlanmıştır.
* **Sahnede Neden Çalışmıyor?** [`Stage1CreatureSimulationBridge.StepAggroAgent` (satır 150–181)](../Assets/_Stratocraft/Character/Stage1CreatureSimulationBridge.cs#L150-L181) içinde `DestructibleNavSpanAStar.FindPath` **hiç çağrılmamaktadır!** Bunun yerine:
  ```csharp
  Vector3 flatDir = new Vector3(playerPos.x - pos.x, 0f, playerPos.z - pos.z);
  Vector3 nextPos = pos + flatDir * (a.BaseSpeed * a.SpeedMultiplier * deltaTime);
  ```
  şeklinde dümdüz `flatDir` vektörüyle yürütülmekte; önüne `1.25m`'den yüksek bir duvar çıktığında ise A* ile yol aramak veya o duvarı kırmak yerine rastgele yan teğet (`tangent = (-flatDir.z, 0, flatDir.x)`) yönüne kaymaktadır (satır 176–179). Ayrıca `DestructibleNavSpanAStar` yalnızca tek bir `VoxelChunkComponent` (`0..15` yerel koordinat) kabul edecek şekilde yazılmıştır; oysa canlı sahnede dünya `Dictionary<(int cx, int cz), uint[]> worldChunks` üzerinde çoklu chunk (`Multi-Chunk World Coordinates`) olarak tutulmaktadır.

### Kök Neden 2: `TARGET_SUPPORT_PILLARS` Kuşatmasının Yanlış Konumu Kırması ve Hasar Kademesini (`DamageState 0..15`) Atlaması
* **Sahnede Ne Yapıyor?** [`Stage1CreatureSimulationBridge.ExecuteSupportPillarSiegeStrike` (satır 229–254)](../Assets/_Stratocraft/Character/Stage1CreatureSimulationBridge.cs#L229-L254) içinde:
  1. Kırılacak bloğun X ve Z koordinatı olarak yaratığın kendi önündeki sütun değil, doğrudan oyuncunun X/Z koordinatı (`wx = FloorToInt(playerPos.x)`, `wz = FloorToInt(playerPos.z)`) alınmaktadır.
  2. [`Creature21BehaviorSystem.TryExecuteSupportPillarStrike` (satır 296–334)](../Assets/_Stratocraft/CreaturesAI/CreaturesAndBosses/Creature21BehaviorSystem.cs#L296-L334) metodundaki `4-bit DamageState (0..15)` kademeli hasar birikimi ve `VoxelSiegeDps` hesabı hiç kullanılmadan, `WriteWorldVoxelRaw(..., 0u)` ile blok tek vuruşta anında silinmektedir.
  3. Yalnızca `OyuncuItemBlogu` kontrol edilmekte, oyuncunun üzerine çıktığı doğal kaya sütunları veya duvar barikatları A* rotası üzerindeki `IsBreachRequired` düğümleriyle ilişkilendirilmemektedir.

### Kök Neden 3: `BossCatalogAndPhoenixSystem` Dosyasının Yokluğu ve Phoenix Boss'un `0 HP`'de Kor Yumurta Olmak Yerine Yan Yatıp Ölmesi
* **Kök Neden:** Kod tabanında `Assets/_Stratocraft/CreaturesAI/Bosses/BossCatalogAndPhoenixSystem.cs` sınıfı **henüz hiç oluşturulmamıştır** (dizinde yalnızca `Combat`, `CreaturesAndBosses`, `Data`, `HybridLod`, `Pathfinding` klasörleri mevcuttur).
* **Sahnedeki Hatalı Davranış:** [`Stage1CreatureSimulationBridge.TryApplyPlayerWeaponSlash` (satır 275–280)](../Assets/_Stratocraft/Character/Stage1CreatureSimulationBridge.cs#L275-L280) içinde yaratığın `IsFlyingBoss` (Anka Kor Avatar) olup olmadığına bakılmaksızın:
  ```csharp
  if (a.CurrentHp <= 0f)
  {
      a.State = CreatureAiState.Dead;
      a.Root.localRotation = Quaternion.Euler(88f, a.Root.eulerAngles.y, 15f);
  ```
  çalışmakta ve Phoenix Boss `0 HP` olduğunda Kor Yumurtaya (`10 sn / 2.500 HP`) dönüşmek yerine sıradan bir mob gibi `88°` yan yatıp ölmektedir!

### Kök Neden 4: 21 Yaratık Sürü Ekolojisi (`Boids` + `HyperbolicLanchesterCombat` Moral/Kaçma) Bağlantısının Eksikliği
* **Kök Neden:** [`HyperbolicLanchesterCombat.cs`](../Assets/_Stratocraft/CreaturesAI/Combat/HyperbolicLanchesterCombat.cs) arka planda yazılmış olmasına rağmen [`Stage1CreatureSimulationBridge.cs`](../Assets/_Stratocraft/Character/Stage1CreatureSimulationBridge.cs) içinde hiç çağrılmamaktadır. Sahnedeki `PackAlpha`, `Flanker01` ve `Flanker02` birbirlerinin konumundan, canından ve sürü moralinden habersizdir; `Flee` (kaçma) veya `Flank` (kanattan sarma) davranışı yoktur.

---

## 4. Kurulacak Gerçek Algoritma ve Canlı Sahne Entegrasyon Mimarisi

Tespit edilen tüm kök nedenleri ortadan kaldırmak için aşağıdaki **4 Bütünleşik Mimari Modül** doğrudan canlı sahneye ve `CreaturesAI` katmanına entegre edilecektir:

```mermaid
flowchart TD
    subgraph SceneBridge["Stage1CreatureSimulationBridge (Canli Sahne Koprusu)"]
        A["StepCreaturesAi (Her Kare)"] --> B["Boids + HyperbolicLanchester Sürü & Moral Degerlendirici"]
        B -->|Moral Kirildi / Can < %25| C["CreatureAiState.Flee (Dağılma + Kaçış Vektörü)"]
        B -->|Aggro Menzili İçi| D["Multi-Chunk DestructibleNavSpanAStar.FindWorldPath"]
        D -->|Yürünebilir Düğüm| E["3B Voxel Rota Takibi + Basamak Zıplama/İnme"]
        D -->|IsBreachRequired Düğümü veya ΔY >= 2.2m| F["SupportPillarSiege (Kademeli 4-bit DamageState + Domino Tetikleme)"]
    end

    subgraph PhoenixLoop["BossCatalogAndPhoenixSystem (Phoenix 3-Evreli Döngü)"]
        G["Evre 1: Hava/Kara Boss (MaxHp)"] -->|HP <= 0 (İlk Çöküş)| H["Evre 2: Kor Yumurta (2.500 HP, 10.0 sn Sayaç)"]
        H -->|10 sn İçinde 2.500 HP Kırıldı| I["Kalıcı Ölüm (CreatureAiState.Dead)"]
        H -->|10 sn Doldu ve Yumurta Sağlam| J["Evre 3: Yeniden Doğuş (%60 HP, 1.5x Hasar, Uçuş Aktif)"]
    end
```

### 4.1. Dünya Koordinatlı Çoklu-Chunk `DestructibleNavSpanAStar` Entegrasyonu
1. **`DestructibleNavSpanAStar.FindWorldPath` Aşırı Yüklemesi (`Overload`):**
   * Mevcut `VoxelChunkComponent` (tek chunk) imzası geriye dönük test uyumluluğu için korunurken, canlı sahnenin `Dictionary<(int cx, int cz), uint[]> worldChunks` yapısı üzerinde çalışan **Dünya Koordinatlı (`WorldVoxelCoord`)** ve **Sıfır-GC Önbellekli (`Reusable Buffers`)** `FindWorldPath` metodu eklenecektir.
   * **3B Hareket + Basamak/Düşüş Kuralları:** Komşu düğüm genişletmesinde yalnızca düz `(dx, dz)` değil; `+1` blok yukarı basamak çıkma (`Step-Up`), `-1..-3` blok güvenli aşağı inme (`Safe Drop`) ve önünde katı blok varsa `VoxelSiegeDps` üzerinden kırma maliyeti ($t_{\text{breach}} = \frac{100 \cdot (16 - \text{DamageState})}{\text{SiegeDPS}}$) hesaplanacaktır.
   * **Zaman Dilimli Rota Yenileme (`Path Recalculation Interval = 0.25 sn`):** Her ajan kendi `List<PathNodeCoord> ActivePath` ve `bool NextNodeRequiresBreach` durumunu tutar; kare başına sıfır GC tahsisiyle rotasını takip eder.

### 4.2. Blok Kırma Maliyetli `TARGET_SUPPORT_PILLARS` Taşıyıcı Kolon Kuşatması
1. **Gerçek Kolon ve Barikat Tespiti:**
   * Ajan oyuncuya yatayda yaklaştığında ancak dikey fark $\Delta y \ge 2.2\text{ m}$ olduğunda (veya A* rotası üzerindeki bir sonraki düğüm `IsBreachRequired == true` olduğunda), `FindNearestSupportPillarOrBarrierVoxel` metodu ile oyuncunun altındaki kulenin/sütunun tabanındaki (`baseY..baseY+2`) taşıyıcı voksel (hem oyuncu yapısı `OyuncuItemBlogu` hem de yüksek kule oluşturan katı sütun blokları) tespit edilir.
2. **Kademeli `4-Bit DamageState (0..15)` Kuşatma Vuruşu + Domino Yıkımı:**
   * Her saldırı vuruşunda bloğun `DamageState` değeri yaratığın `VoxelSiegeDps` gücüne orantılı olarak artırılır (`VoxelBitUtility.WithDamageState`).
   * `DamageState > 15` olduğunda blok kırılır (`0u`), `dirtyChunks` güncellenir ve `collapseBridge.EvaluateCollapseAroundSeed` çağrılarak üstteki kulenin/binanın oyuncunun altında **Domino etkisiyle çökmesi** tetiklenir.

### 4.3. 21 Yaratık Sürü/Kaçma Ekolojisi (`Boids` + `HyperbolicLanchesterCombat`)
1. **Sürü Vektörleri (`Separation`, `Cohesion`, `Flank Angle`):**
   * `Ash Stalker` sürüsündeki ajanlar (`PackAlpha`, `Flanker01`, `Flanker02`) birbirleriyle çarpışmamak için `Separation` itme kuvveti ve oyuncuyu $\pm 45^\circ$ açıdan kuşatmak için `Flank` ofseti uygular.
2. **`HyperbolicLanchesterCombat` Tabanlı Dinamik Moral ve Kaçma (`Flee`):**
   * Sürü üyelerinin toplam kalan canı ve DPS gücü ile oyuncunun gücü `HyperbolicLanchesterCombat.SolveCombat` üzerinden değerlendirilir.
   * Eğer ` SolveCombat` sonucunda sürünün yenileceği kesinleşirse VE ajanın kendi canı `< %25` ise (veya sürü lideri `PackAlpha` öldürüldüyse ve ajan ağır yaralıysa), ajan `CreatureAiState.Flee` durumuna geçerek oyuncudan ters yönde hızla uzaklaşır.

### 4.4. `BossCatalogAndPhoenixSystem` ve Phoenix `10 sn / 2.500 HP` Kor Yumurta Döngüsü
1. **`Assets/_Stratocraft/CreaturesAI/Bosses/BossCatalogAndPhoenixSystem.cs` Sınıfı:**
   * Kanonik **13 Ritüel Boss Kataloğunu** (`BossArchetypeDefinition`) ve **Phoenix Kor Yumurta Durum Makinesini (`PhoenixBossState`: `AerialOrGroundPhase1`, `EmberEggPhase2`, `RebornEnragedPhase3`, `PermanentlyDead`)** barındırır.
   * Sabitler: `EmberEggMaxHp = 2500f`, `EmberEggDurationSeconds = 10.0f`, `RebirthHpRatio = 0.60f`, `RebirthDamageMultiplier = 1.50f`.
2. **Canlı Sahne (`Stage1CreatureSimulationBridge`) Entegrasyonu:**
   * Phoenix Boss ilk kez `0 HP`'ye düştüğünde `CreatureAiState.EmberEgg` durumuna geçer: Canı **`2.500 HP`** olur, `10.0 sn` sayaç başlar, model yere çöker ve altın/kor ölçek (`localScale` büzülmesi + kor titreşimi) alır.
   * Oyuncu `10.0 sn` dolmadan kılıç darbeleriyle `2.500 HP`'yi bitirirse Phoenix kalıcı olarak ölür (`Dead`).
   * `10.0 sn` dolarsa Phoenix **`%60 HP`** ve **`1.5x` saldırı hasarı** ile yeniden doğar (`RebornEnragedPhase3`), HUD üzerinde `"ANKA KULLERINDEN DOGDU! (%60 HP | 1.5x Hasar)"` bildirimi gösterilir.
