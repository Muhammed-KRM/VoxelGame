# Performans Raporu 4: Sürü (Horde) Canlı AI, 3B Yol Bulma ve Binlerce Online Oyuncu Ağ (Netcode) Mimarisi Araştırması

> **Kapsam:** Bu rapor; yıkılabilir voksel dünyalarda yüzlerce/binlerce yaratığın (sürü/horde) aynı anda hareket etmesi, yol bulması ve çizilmesi ile binlerce çevrimiçi oyuncunun aynı dünyada kasma (CPU/GPU/Bant Genişliği darboğazı) olmadan etkileşime girmesini sağlayan endüstri standardı mimarileri, GDC teknik sunumlarını, açık kaynak çözümleri ve projemizde zaten kurulu olan Unity 6 DOTS (`Entities`, `Burst`, `Collections`, `Entities.Graphics`, `Netcode for Entities`) paketleriyle tekerleği yeniden icat etmeden nasıl uygulanacağını inceler.

---

## 1. Yıkılabilir Voksel Dünyalarda ve Dev Sürü Oyunlarında AI & Yol Bulma Mimarisi

Geleneksel oyunlarda statik bir `NavMesh` (Gezinme Ağı) oyun öncesinde (bake aşamasında) bir kez hesaplanır. Ancak **7 Days to Die**, **Deep Rock Galactic**, **Factorio** veya **Stratocraft** gibi her bloğun kırılabildiği, tünellerin kazıldığı ve klan kalelerinin inşa edildiği dinamik dünyalarda yüzlerce canlı için geleneksel `NavMesh` veya bireysel `A*` çalıştırmak ana iş parçacığını (Main Thread) anında kilitler.

Endüstrideki başarılı oyunlar bu sorunu **dört temel mimari katman** ile çözer:

### 1.1. 7 Days to Die: Ağırlıklı Voksel Maliyet Alanı ve Kademeli AI (Sleeper & Horde Volumes)
* **Sorun:** Kanlı Ay (Blood Moon) gecesinde onlarca/yüzlerce zombi oyuncunun inşa ettiği çok katmanlı kaleye saldırırken her zombinin ayrı ayrı "hangi duvarı kırsam daha hızlı ulaşırım?" hesabı yapması.
* **Çözüm Mimarisi:**
  1. **Voksel Dayanıklılık Ağırlıklı Maliyet Grafı (Block HP Cost Integration):** Yol bulma ızgarasında boş hava hücresinden geçmenin maliyeti `1` iken, ahşap kapı hücresinin maliyeti `15`, güçlendirilmiş çelik/beton bloğun maliyeti `80+` olarak işaretlenir. Böylece yol bulucu ayrı bir "duvar yıkma AI'ı" çalıştırmak yerine, doğal olarak en zayıf kapıya veya gedik açılmış duvara yönelir; açık yol yoksa en düşük toplam maliyetli duvarı kemirmeye başlar.
  2. **Kirli Alt-Yığın (Dirty Sub-Chunk) Güncellemesi:** Oyuncu bir bloğu kırdığında veya klan çiti diktiğinde tüm dünya yeniden hesaplanmaz. Sadece değişen `16x16x16` alt-yığın `Dirty` işaretlenir ve maliyet alanı yalnızca o yerel bölgede güncellenir.
  3. **Zaman Dilimli (Time-Sliced) Yol Kuyruğu ve Sleeper Volumes:** Uzaktaki binalarda veya mağaralarda bulunan yüzlerce zombi "Sleeper" (uyuyan veri kaydı) olarak tutulur; ne fizik motorunda ne de `Update()` döngüsünde yer kaplarlar. Oyuncu yalnızca ilgili hacme (Volume) yaklaştığında aktif varlığa dönüştürülürler.

### 1.2. Deep Rock Galactic: 3B Yüzey/Tavan Gezinmesi ve Seyrek Voksel Oktree (Sparse Voxel Octree)
* **Sorun:** Tamamen yıkılabilir 3B mağaralarda yüzlerce *Glyphid* böceğinin sadece düz zeminde değil, dikey duvarlarda ve mağara tavanında baş aşağı sürüler halinde kasmadan akması.
* **Çözüm Mimarisi:**
  1. **3B Seyrek Oktree ve Yüzey Komşuluk Grafı:** Dünya düz bir 2B yükseklik haritası (Heightmap) olarak değil, dolu voksel yüzeylerinin boş hava vokselleriyle temas ettiği **Yüzey Kabuğu (Surface Shell Graph)** olarak modellenir. Bir dolu vokselin 6 yönünden (üst, alt, kuzey, güney, doğu, batı) herhangi biri hava ise o yüzey "yürünebilir düğüm" kabul edilir.
  2. **Hedef Merkezli 3B Akış Alanı (Flow Field):** Cüceler (oyuncular) az sayıda (1-4 grup) olduğu için, her böcekten oyuncuya `A*` aramak yerine **oyuncuların konumundan dışarıya doğru yayılan 3B Vektör Alanı** hesaplanır. Tüneller kazıldığında yerel oktree düğümleri güncellenir ve yüzlerce böcek $O(1)$ tablo okumasıyla duvarlardan/tavandan oyuncuya akar.
  3. **Prosedürel Animasyon LOD'u:** Yakındaki böcekler yüzeye dik (Surface Normal) hizalanma ve prosedürel bacak IK'sı çalıştırırken, orta/uzak mesafedeki sürü üyeleri yalnızca yüzey normaline göre döndürülmüş tek bir GPU matrisi ve önceden fırınlanmış (baked) animasyonla kayar.

### 1.3. They Are Billions, World War Z (Swarm Engine) ve Space Marine 2: 10.000+ Sürü Mimarisi
* **Sorun:** Aynı ekranda `5.000` ila `20.000` zombi veya *Tyranid* yaratığının dar geçitlerden taşarak, birbirinin içinden geçmeden ve 60 FPS altına düşmeden saldırabilmesi.
* **Çözüm Mimarisi:**
  1. **Sıfır Bireysel A* — Tamamen Küresel Flow Field:** *They Are Billions* haritayı hücrelere böler ve oyuncu üssünden dışarıya doğru **Dijkstra Mesafe Alanı (Integration Field)** üretir. `20.000` zombinin tamamı aynı akış tablosunu okur. Yol bulma maliyeti canlı sayısından ($N$) tamamen bağımsızdır ($O(1)$).
  2. **Hibrit Görsel/Fiziksel Varlık (Impostor VAT $\leftrightarrow$ Gerçek İskelet Geçişi):** *World War Z* ve *Space Marine 2* (Saber Interactive Swarm Engine) uzaktaki ve sürü ortasındaki binlerce yaratığı **CPU iskeleti olmayan GPU VAT (Vertex Animation Texture)** örnekleri (instances) olarak çizer. Sadece oyuncunun kılıç/mermi menziline (`< 10-15m`) giren veya bomba ile havaya uçan yaratıklara anlık olarak havuzdan (Pool) gerçek fiziksel `Ragdoll` / `Hitbox` atanır.

### 1.4. Factorio (Friday Facts #317 & #345): Hiyerarşik Chunk Grafı, Sürü Grupları ve "Entity Sleep"
* **Sorun:** Devasa bir haritada binlerce *Biter* böceğinin göllerin ve oyuncu fabrikalarının etrafından dolaşarak kirlilik (pollution) kaynağına saldırması sırasında UPS (tick hızı) düşüşü.
* **Çözüm Mimarisi:**
  1. **İki Katmanlı Hiyerarşik Yol Bulma (Chunk Perimeter Graph):** Harita `32x32` chunk'lara bölünür. Üst katman oyuncu binalarını tamamen yok sayar ve sadece doğal arazi (su/uçurum vs kara) üzerinden chunk kenarlarının (perimeter) birbirine bağlantısını tutar. Bu üst graf çok küçük olduğu için makro rota mikrosaniyeler içinde bulunur ve yerel `A*` aramasına kusursuz bir sezgisel (heuristic) kılavuzluk sağlar.
  2. **Sürü Grupları (Unit Groups):** 100 böcek saldırıya geçtiğinde 100 ayrı yol aranmaz. Bir **Grup Lideri / Sürü Çapası (Swarm Anchor)** yolu bulur; diğer 99 böcek sadece grup merkezini ve yerel akış/boids ayrışma kurallarını takip eder.
  3. **Agresif Uyku Durumu (Entity Sleeping):** Oyuncu veya kirlilik bulutu yaklaşmadığı sürece uzaktaki tüm yuvalar ve canlılar aktif güncelleme listesinden tamamen çıkarılır (`0 CPU Tick`).

---

## 2. Teknik Derinlik: 4 Kritik Sürü (Horde) Optimizasyon Tekniği

### 2.1. 3D Flow Field (Akış Alanı / Dijkstra Vector Field) vs Bireysel A*

#### Neden Bireysel A* Sürülerde Çöker?
200 canlı aynı oyuncuya veya klan kristaline saldırırken her canlı için ayrı `A*` çalıştırmak:
$$\text{Maliyet}_{A^*} = N_{\text{canlı}} \times O(E \log V)$$
Üstelik oyuncu hareket ettiğinde veya bir blok kırıldığında bu 200 yolun tamamı geçersiz (invalid) olur ve yeniden hesaplanması gerekir.

#### 3B Akış Alanı (Flow Field) Nasıl Çalışır?
Hedef (Oyuncu veya Klan Kristali) sayısı az ($M \ll N$), saldıran canlı sayısı çok ($N \gg 100$) olduğunda arama yönü tersine çevrilir: **Canlıdan hedefe değil, hedeften dışarıya doğru tek bir dalga yayılır.**

```mermaid
flowchart LR
    A["1. Cost Field (3B Voksel Maliyet Tablosu)<br/>byte[X*Y*Z]<br/>Yürünebilir=1, Yamaç=3, Duvar=50, Engel=255"] --> B["2. Integration Field (Dijkstra / Dial's Bucket BFS)<br/>ushort[X*Y*Z]<br/>Hedef hücreden (0) dışarıya toplam mesafe dalgası"]
    B --> C["3. Flow Field (Yön Vektör Tablosu)<br/>byte[X*Y*Z] veya half3[X*Y*Z]<br/>Her hücreden en düşük maliyetli komşuya birim vektör"]
    C --> D["4. Sürü Ajanları (1.000+ Canlı)<br/>O(1) Dizi Okuması<br/>velocity = flowField[voxelIndex] + separation"]
```

1. **Adım 1 — Maliyet Alanı (Cost Field - `NativeArray<byte>`):**
   Oyuncunun etrafındaki yerel 3B hacim (örneğin `64 x 32 x 64 = 131.072` hücre, her hücre `1x1x1m` veya `2x2x2m`) taranır. Altı katı zemin, üstü 2 blok hava olan hücreler `1` (yürünebilir), 1 blok basamak çıkışı olanlar `2`, klan çiti/duvarı olanlar yapı sağlamlığına göre `20..200`, lav/uçurum/tavan olanlar `255` (geçilemez) olarak işaretlenir.
2. **Adım 2 — Entegrasyon Alanı (Integration Field - `NativeArray<ushort>`):**
   Oyuncunun (veya klan kristalinin) bulunduğu hücreye `0` yazılır ve bir FIFO kuyruğu (veya projemizde yapısal çöküşte de kullanılan **Dial's Bucket Queue**) ile komşu hücrelere doğru dalga yayılır:
   $$\text{Integration}[n] = \min \left( \text{Integration}[c] + \text{Cost}[n] \right)$$
   Darboğaz veya öncelik kuyruğu (`BinaryHeap`) sıralama maliyeti olmadan $O(V)$ doğrusal sürede tamamlanır.
3. **Adım 3 — Akış Alanı (Flow Field - `NativeArray<byte>`):**
   Her hücre, 26 komşusu (veya yatay+basamak 18 komşusu) içinden `Integration` değeri en küçük olan komşunun yön indisini (`0..25`, sadece `1 byte`!) kaydeder.
4. **Adım 4 — $O(1)$ Sürü Okuması:**
   1.000 canlının her biri bulunduğu konumu tam sayıya yuvarlar (`int3 cell = (int3)math.floor(pos)`), düz dizi indisini hesaplar ve hangi yöne koşacağını **tek bir bellek okumasıyla ($O(1)$)** öğrenir!

| Metrik | 200 Canlı İçin Bireysel 3B A* | Tek 3B Flow Field (`64x32x64` Burst Job) | Kazanım |
| :--- | :--- | :--- | :--- |
| **Yol Bulma Karmaşıklığı** | $O(N \cdot E \log V)$ (Canlı sayısıyla çarpanlı) | $O(V) + O(N)$ (Canlı sayısından bağımsız) | **100x - 500x** |
| **200 Canlı CPU Süresi** | `~18.0 ms - 45.0 ms` (Kare düşüşü / takılma) | `~0.35 ms` (Arka plan Worker Thread'de 4 Hz) | **Ana Thread 0.02 ms** |
| **1.000 Canlı CPU Süresi** | `> 150 ms` (Oyun kilitlenir) | `~0.38 ms` (Sadece +800 adet $O(1)$ dizi okuması) | **Sınırsız Ölçeklenme** |
| **Bellek Ayırma (GC Alloc)** | Her aramada Node/List tahsisi riski | Sabit boyutlu kalıcı `NativeArray` (`~384 KB`) | **0 B / kare** |

---

### 2.2. Spatial Hashing (`NativeParallelMultiHashMap<int, int>`) vs $O(N^2)$ Boids

Yüzlerce canlı aynı `Flow Field` vektörünü takip ettiğinde dar geçitlerde üst üste binerler (clumping). Birbirlerini itmeleri (Separation) ve yerel kaçınma (Local Avoidance / RVO) yapmaları gerekir.

* **Naif $O(N^2)$ Yaklaşımın Çöküşü:** Her canlının diğer tüm canlılarla mesafesini kontrol etmesi: $1.000 \times 1.000 = 1.000.000$ kontrol/kare.
* **3B Uzamsal Karma Tablosu (Spatial Hashing) ile $O(N)$ Çözüm:**
  Unity `com.unity.collections` paketindeki `NativeParallelMultiHashMap<int, int>` kullanılarak her karede iki küçük Burst işi (`IJobParallelFor`) çalıştırılır:

```csharp
// 1. AŞAMA: Her canlıyı bulunduğu 3B hücre anahtarına paralel olarak kaydet (O(N))
[BurstCompile]
public struct PopulateSpatialHashJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float3> Positions;
    public float InvCellSize; // Örn: 1.0f / 2.5f (2.5m hücre boyutu)
    public NativeParallelMultiHashMap<int, int>.ParallelWriter HashGrid;

    public void Execute(int index)
    {
        int3 cell = (int3)math.floor(Positions[index] * InvCellSize);
        int hash = (int)math.hash(cell);
        HashGrid.Add(hash, index);
    }
}

// 2. AŞAMA: Sadece komşu hücrelerdeki (en fazla K=8) canlılarla itişme hesapla (O(N * K))
[BurstCompile]
public struct SteerAndSeparateJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float3> Positions;
    [ReadOnly] public NativeParallelMultiHashMap<int, int> HashGrid;
    public float InvCellSize;
    public float SeparationRadiusSq;
    [WriteOnly] public NativeArray<float3> OutSteering;

    public void Execute(int index)
    {
        float3 myPos = Positions[index];
        int3 baseCell = (int3)math.floor(myPos * InvCellSize);
        float3 separation = float3.zero;
        int neighborCount = 0;
        const int MaxNeighbors = 8; // Erken çıkış sınırı: yoğun sürüde bile O(1) üst sınır!

        // Yatay düzlemde 3x3 (veya 3B'de 3x3x3) komşu hücreleri tara
        for (int dz = -1; dz <= 1 && neighborCount < MaxNeighbors; dz++)
        {
            for (int dx = -1; dx <= 1 && neighborCount < MaxNeighbors; dx++)
            {
                int hash = (int)math.hash(baseCell + new int3(dx, 0, dz));
                if (HashGrid.TryGetFirstValue(hash, out int otherIdx, out var it))
                {
                    do
                    {
                        if (otherIdx == index) continue;
                        float3 diff = myPos - Positions[otherIdx];
                        float distSq = math.lengthsq(diff);
                        if (distSq > 0.0001f && distSq < SeparationRadiusSq)
                        {
                            separation += diff * math.rsqrt(distSq) * (1.0f - distSq / SeparationRadiusSq);
                            neighborCount++;
                            if (neighborCount >= MaxNeighbors) break;
                        }
                    } while (HashGrid.TryGetNextValue(out otherIdx, ref it));
                }
            }
        }
        OutSteering[index] = separation;
    }
}
```
* **Kritik Mühendislik İpucu (`MaxNeighbors = 8` Erken Çıkışı):** 200 yaratık dar bir mağara ağzına sıkışsa bile bir yaratığın etrafını en fazla 6-8 yaratık sarabilir. `neighborCount >= 8` olduğunda döngüden çıkmak, en kötü senaryoda (worst-case clustering) bile karmaşıklığın $O(N^2)$'ye bozulmasını kesin olarak engeller ve **1.000 canlı için tüm çarpışma/ayrışma hesabını Burst ile `~0.08 ms` altında** bitirir!

---

### 2.3. GPU Vertex Animation Textures (VAT) + `RenderMeshInstanced` / `BatchRendererGroup`

#### Neden Hiyerarşik `GameObject` + `Animator` Sürülerde Kullanılamaz?
Bir yaratığı Gövde + Baş + 4 Bacak + Kuyruk gibi 12-15 alt `GameObject` (`Transform`) olarak oluşturup CPU'da her kare `localRotation` değiştirmek:
* `500 canlı x 15 Transform = 7.500 Transform` hiyerarşisi demektir.
* Unity'nin C++ tarafındaki `TransformAccessArray` kirli matris yayılımı (dirty matrix propagation) ve `500 x 15 = 7.500` ayrı çizim çağrısı (Draw Call / Dynamic Batching kırılması) CPU Render Thread'i tamamen boğar.

#### Çözüm: Tek Birleşik Mesh + GPU VAT (veya Shader Pivot Animasyonu)
1. **Tek Birleşik Statik Mesh (Single Merged Mesh):**
   Canlının tüm uzuvları (gövde, bacaklar, çene, kanatlar) üretim aşamasında **tek bir `Mesh`** içinde birleştirilir. Her köşe noktasının (vertex) hangi uzva ait olduğu köşe renginin alfa kanalına (`Color32.a = PartId`) veya `UV2.x = VertexIndex` kanalına yazılır.
2. **Seçenek A — Prosedürel Shader Uzuv Rotasyonu (0 KB Doku Maliyeti, `< 16 MB VRAM` Bütçe Dostu):**
   Yürüme, koşma ve saldırma döngüleri zaten sinüzoidal sarkaç hareketleridir. Köşe (Vertex) shader'ına örnek başına (Per-Instance) sadece `float4(_AnimPhase, _AnimSpeed, _AttackBlend, _HurtFlash)` gönderilir. Vertex shader, `PartId`'ye göre bacak/kanat köşelerini kendi yerel pivot noktaları etrafında doğrudan GPU'da döndürür! **VRAM maliyeti `0 Byte`'tır.**
3. **Seçenek B — Vertex Animation Texture (VAT - Karmaşık Boss/Yaratık Animasyonları İçin):**
   - **X Ekseni:** Mesh'in Vertex indisi (örn. `512` vertex).
   - **Y Ekseni:** Animasyon kareleri (örn. `64` kare: `0..15` Idle, `16..31` Run, `32..47` Attack, `48..63` Die).
   - **Boyut:** `512 x 64` `RGBAHalf` (veya sıkıştırılmış `RGBA32` + Bound Min/Max) doku sadece **`64 KB - 128 KB` VRAM** tüketir! Projemizin `< 16.0 MB` toplam VRAM bütçesinin binde birini bile harcamaz.
4. **`Graphics.RenderMeshInstanced` ve `BatchRendererGroup` (BRG):**
   Aynı türdeki 500 yaratık için CPU'dan GPU'ya sadece `NativeArray<Matrix4x4>` (veya BRG `float3x4` sıkıştırılmış matris + `float4` animasyon fazı) yüklenir. **500 yaratık tek bir (1) Draw Call ile çizilir!**

#### 3 Kademeli Simülasyon ve Görsel LOD (`OffScreenEntityRecord` Geçişi)

| Kademe | Mesafe / Koşul | Görsel Temsil (GPU) | Fizik & AI Temsili (CPU) | Kare Başına Maliyet |
| :--- | :--- | :--- | :--- | :--- |
| **LOD 0 (Yakın Çatışma)** | `0m - 30m` ve Kamera Görüşünde (Maks 16-24 Canlı) | VAT / GPU Shader + Tam Gölge + Silah Çarpışma (Forte/Foible) Hassas Kontrolleri | 240 Hz / 60 Hz Hassas Hitbox, Yerel RVO Separation, Blok/Klan Çiti Kırma | Canlı başına `~0.015 ms` |
| **LOD 1 (Görünür Sürü)** | `30m - 120m` ve Kamera Frustum İçinde (`100 - 1.000` Canlı) | `RenderMeshInstanced` / `BRG` (Tek Draw Call, Basit Gölge veya Blob Shadow) | Sadece Burst Struct: $O(1)$ Flow Field + Spatial Hash Separation + Voksel Yükseklik Okuması (`0 GameObject`, `0 Collider`) | 1.000 canlı toplam `~0.15 ms` |
| **LOD 2 (Analitik Tablo / Uyku)** | `> 120m` veya Mağara/Dağ Arkasında (`OffScreenEntityRecord`) | **Sıfır Çizim** (GPU'ya hiçbir veri gönderilmez) | `NativeArray<OffScreenEntityRecord>`: Saniyede 1 kez (`1 Hz`) makro grup vektörüyle ilerler veya `Sleep` modunda bekler | 5.000 kayıt toplam `< 0.02 ms` |

---

## 3. Binlerce Online Oyuncu ve Canlı Ağ (Multiplayer Netcode) Optimizasyonu

Binlerce oyuncunun ve binlerce canlının bulunduğu bir dünyada (örneğin **Minecraft Folia/Minestom**, **Rust**, **Foxhole**, **Ashes of Creation**, **Pax Dei**) sunucunun ve istemcinin kasmaması, ağ trafiğinin $O(P^2)$ (Oyuncu $\times$ Oyuncu) olmaktan çıkarılıp **sıkı bir bant genişliği bütçesine (istemci başına `< 50-100 KB/sn`)** hapsedilmesine dayanır.

### 3.1. Spatial Interest Management (Area of Interest / AOI Hücreleri)
* **Temel İlke:** Bir oyuncu haritanın kuzeyindeki buz biyomundayken, güneydeki volkan biyomunda yürüyen 500 oyuncunun ve 2.000 yaratığın ağ paketlerini **kesinlikle almamalıdır**.
* **Nasıl Çalışır?**
  1. Dünya `64m x 64m` (veya `32m x 32m`) boyutunda **AOI Ağ Hücrelerine (Network Grid Cells)** bölünür.
  2. Her bağlantı (`NetworkConnection`), bulunduğu hücre ve etrafındaki görünür yarıçap (örn. $3 \times 3$ veya $5 \times 5$ hücre) içindeki varlık listesine abone olur.
  3. **Sınır Titremesi Önleme (Hysteresis Border):** Bir varlık AOI sınır çizgisinde ileri-geri yürürken sürekli `Spawn` / `Despawn` paketi göndermemesi için **Giriş Yarıçapı (`120m`)** ile **Çıkış Yarıçapı (`135m`)** arasında `15m` tampon bölge (buffer zone) bırakılır.
  4. **Dinamik Yoğunluk Daraltması (Ashes of Creation / Foxhole Siege Culling):** Eğer tek bir kale kuşatmasında aynı hücreye 300 oyuncu ve 500 yaratık toplanırsa, sunucu o hücredeki oyuncuların AOI yarıçapını otomatik olarak `120m`'den `60m`'ye daraltır ve paket bütçesini korur.

### 3.2. Mesafe ve Önem Tabanlı Frekans Ölçekleme (Distance & Importance Tick Scaling)
AOI içindeki her varlık aynı hızda güncellenmez. Sunucu `60 Hz` veya `30 Hz` sabit fizik tick'i çalıştırsa bile, ağ gönderim sıklığı (`Send Rate`) mesafeye ve öneme göre bölünür:

| Mesafe / Varlık Türü | Ağ Gönderim Frekansı | İstemci Tarafı Yumuşatma | Neden? |
| :--- | :--- | :--- | :--- |
| **Yakın Çatışma (`0m - 20m`)** | **30 Hz - 60 Hz** (Her Tick) | İstemci Tahmini (Client Prediction) + Kısa Interpolasyon | 240 Hz yönlü yakın dövüşte (Forte/Foible, savuşturma) milisaniyelik kılıç açısı ve pozisyon doğruluğu şarttır. |
| **Orta Mesafe (`20m - 60m`)** | **10 Hz - 15 Hz** (Her 3-4 Tick'te bir) | Snapshot Interpolation (Hermite Spline / Hız Vektörü) | Oyuncu uzaktan koşan düşmanı akıcı görür ama bant genişliği **%75** azalır. |
| **Uzak Görünür (`60m - 140m`)** | **2 Hz - 5 Hz** (Saniyede 2-5 kez) | Doğrusal (Linear) Interpolasyon + Flow Field Tahmini | Uzaktaki silüetler için saniyede 2 güncelleme yeterlidir; bant genişliği **%93** azalır. |
| **Statik Yapılar (Klan Çitleri, Bataryalar, Madenler)** | **0 Hz (Event-Driven / Static)** | Durum değişene kadar sıfır paket | Sadece yapı hasar aldığında, yıkıldığında veya batarya ateşlendiğinde delta paketi gider. |

### 3.3. Bit-Packed Delta Snapshot Sıkıştırma ve Sürü/Voksel Ağ Sırrı

#### A. Bit-Packing ve Kuantizasyon (Quantization)
Ham (Raw) veri göndermek yerine her alan gereken minimum bit sayısına sıkıştırılır:
* **Pozisyon (`float3` = 96 bit $\rightarrow$ 36 bit):** Dünya koordinatını tam `float` göndermek yerine, varlığın bulunduğu Chunk/AOI merkezine göre göreli konumu `0.05m` (`5 cm`) hassasiyetle kuantize edilir (`3 x 12 bit = 36 bit`).
* **Rotasyon (`quaternion` = 128 bit $\rightarrow$ 16 bit):** Yerde yürüyen canlılar ve oyuncular için sadece `Yaw` (yatay dönüş açısı, `10 bit` = `0.35°` hassasiyet) ve `Pitch` (bakış eğimi, `6 bit`) yeterlidir. **%87.5 bit tasarrufu!**
* **Animasyon ve Durum (`byte` / `ushort`):** Tüm alt uzuvların dönüşünü ağdan göndermek yerine sadece `AnimStateId` (`4 bit`) ve `AttackDirection` (`3 bit`) gönderilir; GPU VAT / Prosedürel Shader animasyonu istemcide yerel olarak oynatır.

#### B. Delta Compression (Baseline Fark Sıkıştırması)
Sunucu, her istemcinin en son aldığı onaylı (ACK) ağ karesini (Baseline Snapshot) bilir. Bir sonraki pakette varlığın tüm verisini değil, **sadece bir önceki kareye göre değişen alanların farkını (Delta)** gönderir. Duran veya sabit hızla koşan bir varlık için paket boyutu birkaç bite kadar düşer.

#### C. 1.000 Kişilik Sürüyü (Horde) Ağdan Göndermenin Sırrı: "Swarm Anchor + Flow Field Sync"
*They Are Billions*, *World War Z* ve büyük RTS/Co-op oyunlarının en büyük ağ sırrı şudur: **500 yaratığın her birinin X, Y, Z koordinatını her kare ağdan göndermek yerine, Sürü Grup Çapasını (Swarm Anchor) ve deterministik Flow Field hedefini senkronize etmek!**
* Hem sunucu hem istemci aynı voksel dünyasına ve aynı hedef (oyuncu/kristal) konumuna sahiptir.
* Dolayısıyla istemci de kendi CPU'sunda (Burst Job ile `0.3 ms`'de) aynı `3D Flow Field`'ı hesaplar.
* Uzaktaki/orta mesafedeki sürü üyeleri için sunucu sadece düşük frekanslı (`1-2 Hz`) düzeltme (reconciliation) veya sadece **Sürü Grubu Merkezini + Canlı Maskesini (`BitField`)** gönderir. Yalnızca oyuncunun yakınına (`LOD 0`, `< 25m`) giren 16-24 yaratık yüksek frekanslı bireysel ağ senkronizasyonuna (Ghost) yükseltilir!

#### D. Voksel Dünyası Ağ Senkronizasyonu (Minecraft Paper / Pax Dei Yaklaşımı)
* **İlk Yükleme:** `16x16x16` (`4.096` voksel) alt-yığınlar ham olarak (`16 KB`) gönderilmez. Önce **Bölgesel Blok Paleti (Palette Index: 4-8 bit)** + **RLE (Run-Length Encoding)** uygulanır, ardından **LZ4** ile sıkıştırılarak alt-yığın başına ortalama **`150 Byte - 600 Byte`** boyuta indirilir.
* **Canlı Yıkım ve Domino Çöküşü:** Bir oyuncu kazma vurduğunda veya yapısal çöküş (Domino Collapse) başladığında tüm chunk yeniden gönderilmez; sadece `VoxelDeltaPacket (ChunkCoord + LocalVoxelIndex12Bit + NewVoxelData)` yayınlanır.

### 3.4. Bölgesel Çoklu-Thread Sunucu Tickleme (Minecraft Folia / Region Threading)
* **Geleneksel Sunucu Sorunu:** Standart Minecraft (Vanilla/Spigot) tüm dünyayı tek bir ana thread'de tick'ler. Haritanın bir ucunda 100 kişi devasa bir patlama yaptığında haritanın diğer ucundaki oyuncular da lag (TPS düşüşü) yaşar.
* **Minecraft Folia ve DOTS Çözümü (Region-Based Multithreading):**
  1. Dünya birbirinden bağımsız **Aktif Bölge Adalarına (Active Region Islands)** ayrılır.
  2. Aralarında boş (oyuncusuz) chunk'lar bulunan iki farklı klan bölgesi, birbirinin verisine erişmediği için **tamamen ayrı CPU çekirdeklerinde (Worker Threads) paralel olarak tick'lenir**.
  3. Unity DOTS (`Unity.Entities`) mimarisi doğası gereği `IJobEntity` ve `IJobChunk` üzerinden tüm sistemleri (Fizik, AI Flow Field, Ağ Serileştirme) çok çekirdekli (Multi-threaded) çalıştırdığı için bu bölgesel paralelliği çekirdek düzeyinde destekler.

---

## 4. Benzer Oyunların Karşılaştırmalı Mimari Tablosu

| Oyun / Motor | Maksimum Sürü / Oyuncu Ölçeği | Yol Bulma (Pathfinding) Mimarisi | Çizim & Animasyon (Rendering) | Ağ (Netcode) & Sunucu Mimarisi | Stratocraft İçin Alınacak Ders |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **7 Days to Die** *(Unity)* | ~64-128 Aktif Zombi + Yüzlerce Uyuyan (Sleeper), 8-64 Oyuncu | Voksel Blok HP Ağırlıklı Maliyet Grafı + Zaman Dilimli A* + Dirty Chunk Güncellemesi | Yakında SkinnedMesh, uzakta basitleştirilmiş LOD / Impostor | Chunk Delta Senkronizasyonu + Sleeper Volume Aktivasyonu | **Blok dayanıklılığını (Klan çiti/duvar HP) doğrudan Flow Field maliyet tablosuna (`Cost Field`) ağırlık olarak yazmak.** |
| **Deep Rock Galactic** *(Unreal)* | 100-300+ Glyphid Böceği (Duvar/Tavan dahil), 4 Oyuncu | 3B Seyrek Voksel Oktree + Yüzey/Tavan Normal Grafı + Oyuncu Merkezli Vektör Alanı | Yakında Prosedürel Bacak IK, uzakta basitleştirilmiş yüzey-normali hizalı mesh | Host/Server Otoriter + Voksel CSG Delta + Sürü Dalga (Wave) Spawner | **Mağara zemin, duvar ve tavanlarını 3B yüzey kabuğu olarak indeksleyip 3B Flow Field ile böcekleri tavandan/duvardan akıtmak.** |
| **They Are Billions** *(Özel C++ / DX11)* | **20.000+** Eşzamanlı Zombi | Sıfır Bireysel A*! Tamamen Küresel **Dijkstra Flow Field** + Hücresel İtişme | GPU Instanced Sprite / 2B Atlas + Sıfır İskelet Hiyerarşisi | Deterministik Simülasyon | **100+ canlı aynı hedefe (Oyuncu / Klan Kristali) giderken bireysel A*'ı tamamen kapatıp tek bir Flow Field tablosu okutmak.** |
| **World War Z & Space Marine 2** *(Swarm Engine)* | **5.000 - 10.000** 3B Yaratık, Co-op Çok Oyunculu | Makro Sektör Grafı + Yerel Flow Field + Küme (Flock) Yönlendirmesi | **GPU VAT (Vertex Animation Texture)** + Indirect Instancing; sadece `<15m`'de gerçek İskelet/Ragdoll | Sürü Küme (Swarm Cluster) Senkronizasyonu + Yakın Çatışma Otoriter Hitbox | **Her canlı için 15 alt `GameObject` yerine tek birleşik mesh + GPU VAT/Shader animasyonu ve sadece `<30m`'de tam fizik açmak.** |
| **Factorio** *(Özel C++)* | **10.000+** Aktif Biter + Milyonlarca Fabrika Varlığı, 100+ Oyuncu | `32x32` Chunk Kenar Grafı (Hiyerarşik A*) + **Unit Groups** + **Entity Sleeping** | GPU Atlas Batching | Deterministik Lockstep + Delta Paketleme | **Uzaktaki canlıları `OffScreenEntityRecord` / `Sleep` moduna alıp `%0` CPU tüketmek ve sürüleri `SwarmGroup` lideriyle yürütmek.** |
| **Minecraft (Paper / Folia / Minestom)** *(Java)* | **1.000+** Eşzamanlı Oyuncu, Binlerce Mob | Yerel Voksel A* + Mob Aktivasyon Yarıçapı (`activation-range` ile uzaktakileri dondurma) | Chunk Greedy Meshing + Culling | **Folia Region Threading** (Bölgesel Çoklu Çekirdek) + Paletli LZ4 Chunk Sıkıştırma + Async Netty | **Dünyayı bağımsız bölge adalarına ayırarak kuzeydeki klan savaşının güneydeki oyuncuyu yavaşlatmasını önlemek.** |
| **Rust & Foxhole** *(Unity / Unreal)* | **300 - 1.000** Oyuncu / Sunucu | NavMesh + Sunucu Tarafı AI Uyku Kademeleri | HLOD (Hierarchical LOD) + GPU Instancing | **Grid AOI (`NetworkVisibilityGrid`)** + Mesafe Bazlı Değişken Tick Rate + Bit-Packing | **AOI Hücreleri ve mesafe bazlı gönderim sıklığı (`60 Hz -> 15 Hz -> 2 Hz -> 0 Hz Static`) ile bant genişliğini korumak.** |
| **Ashes of Creation & Pax Dei** *(Unreal 5)* | **1.000 - 10.000** Oyuncu (Server Meshing) | Sunucu Tarafı Hiyerarşik AI LOD | Nanite / GPU Driven Instancing + Impostor | Dinamik AOI Daraltma (Siege Culling) + Voksel/Yapı Delta Akışı | **Yoğun kuşatma anlarında AOI yarıçapını ve uzak canlı güncelleme frekansını dinamik olarak daraltmak.** |

---

## 5. Tekerleği Yeniden İcat Etmeden Uygulama: Projede Kurulu Paketler ve Hazır Çözümler

Projemizin `Packages/manifest.json` dosyası incelendiğinde, dünyadaki en gelişmiş veri-odaklı (Data-Oriented) sürü ve ağ altyapısını kurmak için gereken **tüm resmi paketlerin zaten projede kurulu olduğu** görülmektedir:

| Kurulu Paket (`Packages/manifest.json`) | Sürüm | Sürü AI ve Binlerce Oyuncu Mimarimizdeki Doğrudan Rolü |
| :--- | :--- | :--- |
| `com.unity.entities` | `6.6.0` | `GameObject`/`MonoBehaviour` yükü olmadan 10.000+ canlıyı önbellek dostu (Cache-friendly) `ArchetypeChunk` bellek bloklarında tutar. |
| `com.unity.burst` | `2.0.0` | 3B Flow Field, Spatial Hashing ve RVO kaçınma döngülerini SIMD (AVX2/NEON) makine koduna derleyerek C#'tan **20x-50x daha hızlı** çalıştırır. |
| `com.unity.collections` | `6.6.0` | `NativeArray`, `NativeQueue` ve `NativeParallelMultiHashMap` ile **0 Byte GC Alloc** garantili uzamsal ızgara ve akış alanı tabloları sağlar. |
| `com.unity.entities.graphics` | `6.6.0` | `BatchRendererGroup` (BRG) üzerinden binlerce canlıyı GPU'da tek Draw Call ile çizer; CPU `Transform` hiyerarşisini tamamen ortadan kaldırır. |
| `com.unity.netcode` *(Netcode for Entities)* | `6.6.0` | Yerleşik **Bit-Packing**, **Delta Snapshot (`GhostDeltaPredictor`)**, **AOI (`GhostDistancePartitioningSystem`)** ve **Mesafe Frekans Ölçekleme (`GhostDistanceImportance`)** sistemlerini hazır sunar! |
| `com.unity.physics` | `6.6.0` | Durumsuz (Stateless), Burst-uyumlu paralel `Broadphase` ve `Raycast`/`ColliderCast` sorguları sağlar. |
| `com.unity.ugui` & `com.unity.modules.uielements` | `2.6.0` / `1.0.0` | Binlerce `String` birleştirmesi yerine `UI Toolkit` (Dirty-binding) veya tek bir `Mesh` üzerinden 0 GC Alloc HUD çizimi sağlar. |

### 5.1. Hazır Sistemlerin Doğrudan Kullanım Haritası (Sıfırdan Yazılmasına Gerek Olmayanlar)

1. **Ağda Mesafe Bazlı Frekans Ölçekleme ve AOI İçin Sıfırdan Kod Yazmayın — `Unity.NetCode` Yerleşik Sistemlerini Açın:**
   `com.unity.netcode` (`6.6.0`) içinde aşağıdaki bileşenler zaten **hazır ve Burst ile optimize edilmiş** durumdadır:
   * **`GhostDistancePartitioningSystem`:** Dünyayı otomatik olarak 3B karolara (tiles) böler ve sınır titremesini (border hysteresis) kendisi yönetir.
   * **`GhostConnectionPosition`:** Her oyuncunun bağlantı varlığına eklendiğinde sunucu o oyuncunun odak noktasını otomatik takip eder.
   * **`GhostDistanceImportance`:** Uzaktaki varlıkların ağ gönderim sıklığını (`Send Frequency`) mesafeye göre otomatik düşürür.
   * **`[GhostField(Quantization = 100, Smoothing = SmoothingAction.InterpolateAndExtrapolate)]`:** Source Generator (Kaynak Üretici) derleme zamanında otomatik olarak Bit-Packed Delta Serializer üretir ve istemcide ara değerlemeyi (Interpolation) hazır çalıştırır!
   * **`GhostOptimizationMode.Static`:** Klan çitleri, bataryalar ve tuzaklar için tek bir enum seçimiyle "değişmedikçe 0 bit gönder" modunu aktif eder.

2. **Uzamsal Izgara (Spatial Grid) İçin Sıfırdan Veri Yapısı Yazmayın — `NativeParallelMultiHashMap<int, int>` Kullanın:**
   `com.unity.collections` içindeki `NativeParallelMultiHashMap` ve `Unity.Mathematics.math.hash(int3)` fonksiyonu, 20 satırlık bir `IJobParallelFor` ile saniyede yüz binlerce varlığı 0 GC Alloc ile hücrelere ayırır.

3. **3B Flow Field İçin Mevcut Voksel Altyapımızı (`Dial's Bucket Queue`) Yeniden Kullanın:**
   Projemizde Faz 1'de yapısal çöküş (`Two-Phase Structural Collapse`) için zaten **Dial's Bucket Geodesic Dijkstra** algoritması ve `NativeArray` havuzları bulunmaktadır. Aynı kova kuyruğu (Bucket Queue) mantığı, oyuncunun etrafındaki `64x32x64` voksel hacminde `0.3 ms` içinde 3B Akış Alanı (`FlowFieldVolume`) üretmek için doğrudan kullanılabilir.

4. **Çok Parçalı Canlı Çizimi İçin `GameObject` Hiyerarşisi Kurmayın — Tek Mesh + `StratocraftUberShader` (Vertex PartId/VAT) Kullanın:**
   Projemizin otonom 3B varlık fabrikasında (`Tools/AutonomousAssetFactory/`) üretilen canlıların alt parçaları dışa aktarılırken tek bir `Mesh` olarak birleştirilir. Her uzvun kimliği (`PartId`: Sol Ön Bacak = 1, Sağ Ön Bacak = 2, Baş = 3 vb.) vertex verisine gömülür. Böylece:
   * `GameObject.CreatePrimitive` veya çoklu `Transform` hiyerarşisi tamamen kalkar.
   * `Graphics.RenderMeshInstanced` veya `Entities.Graphics` (`BatchRendererGroup`) ile 1.000 canlı tek çağrıda çizilir.
   * Projemizin `< 16.0 MB` kaplama VRAM bütçesi, `< 2.500 MB` RAM bütçesi ve `0 B/kare` GC Alloc kuralı %100 korunur.

---

## 6. Özet Eylem Planı (Stratocraft İçin Önerilen Mimari Dönüşüm)

```mermaid
flowchart TD
    subgraph Server_Sim["Sunucu / Simülasyon Katmanı (Burst + Jobs)"]
        V["Yıkılabilir Voksel Dünyası<br/>(Dirty Sub-Chunk Takibi)"] --> FF["3B Flow Field (64x32x64)<br/>Oyuncu/Kristal Merkezli (4 Hz, 0.35 ms)"]
        FF --> SH["Spatial Hash Grid (NativeParallelMultiHashMap)<br/>O(N) Yerel Ayrışma (Max 8 Komşu)"]
        SH --> LOD["3 Kademeli Canlı LOD Yöneticisi<br/>LOD 0 (<30m Aktif) | LOD 1 (30-120m Sürü) | LOD 2 (>120m OffScreenTablo)"]
    end

    subgraph Netcode_Layer["Ağ Katmanı (Unity Netcode for Entities 6.6.0)"]
        LOD --> AOI["GhostDistancePartitioningSystem<br/>(AOI Hücre Filtreleme)"]
        AOI --> IMP["GhostDistanceImportance<br/>(Yakın 60Hz, Orta 15Hz, Uzak 2Hz, Yapı 0Hz)"]
        IMP --> BIT["Bit-Packed Delta Snapshot<br/>+ Swarm Anchor Senkronizasyonu"]
    end

    subgraph Client_Render["İstemci Çizim Katmanı (0 GameObject / 0 GC)"]
        BIT --> VAT["Tek Birleşik Mesh + GPU VAT / Vertex PartId Shader<br/>(RenderMeshInstanced / BatchRendererGroup)"]
        VAT --> HUD["Dirty-Checked UI / Tek-Mesh HUD<br/>(0 B/kare GC Alloc)"]
    end
```

1. **Bireysel A* Sınırlaması ve 3B Flow Field Geçişi:**
   Bireysel 3B `A*` sadece tekil özel hedeflere giden (veya mağarada devriye gezen) az sayıdaki canlı için saniyede en fazla `2-4` istek (Time-Sliced Queue) olarak sınırlandırılmalı; oyuncuya veya klan kristaline saldıran tüm sürüler **Oyuncu Merkezli 3B Flow Field** tablosundan $O(1)$ sürede beslenmelidir.
2. **$O(N^2)$ Çarpışma Döngüsünün `NativeParallelMultiHashMap` İle Değiştirilmesi:**
   Canlıların birbirini itmesi (Separation) ve yakın dövüş hedef aramaları `NativeParallelMultiHashMap<int, int>` uzamsal ızgarasına taşınmalı ve hücre başına `MaxNeighbors = 8` sınırı konmalıdır.
3. **Çoklu `GameObject` Canlılardan Tek-Mesh GPU Instancing / VAT Mimarisine Geçiş:**
   Canlı başına 12-15 alt `GameObject` (`Transform`) oluşturmak yerine tek bir birleşik `Mesh` + GPU Vertex Shader animasyonu (`PartId` pivot rotasyonu veya `64 KB` VAT dokusu) ve `RenderMeshInstanced` / `BatchRendererGroup` kullanılmalıdır.
4. **Online Çok Oyunculu Altyapıda `Unity.NetCode` AOI ve Frekans Ölçekleme:**
   Halihazırda kurulu olan `com.unity.netcode` (`6.6.0`) paketinin `GhostDistancePartitioningSystem`, `GhostDistanceImportance` ve `[GhostField]` bit-packing özellikleri doğrudan devreye alınmalı; sürüler için bireysel pozisyon yerine **Swarm Anchor + Flow Field** senkronizasyonu kullanılmalıdır.
