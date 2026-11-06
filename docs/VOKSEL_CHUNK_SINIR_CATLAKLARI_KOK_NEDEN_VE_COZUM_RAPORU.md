# Stratocraft Voksel Chunk Sınır Çatlakları (Seam Cracks) Kök Neden ve Çözüm Raporu

**Rapor Kodu:** `DOC-VOX-SEAM-001`  
**Tarih:** 2026-11-06  
**Durum:** KAPSAMLI ARAŞTIRMA VE KÖK NEDEN TESPİTİ (KOD DEĞİŞİKLİĞİ YAPILMADI - KOD DONDURULDU)  
**İlgili Modüller:** `HybridSurfaceNetsAndShapeMesher.cs`, `Stage1InteractivePlayController.cs`, `StratocraftFloatingIslandWorldGen.cs`, `Stage1MatterCrystalAndMiningController.cs`  

---

## 1. Giriş ve Kullanıcı Geri Bildirimi Analizi

Kullanıcı tarafından paylaşılan son iki oyun içi ekran görüntüsü (`media_1793962637954_6530db2e.jpg` ve `media_1793962653779_bc2c703a.jpg`) incelenmiş, arazide nadir de olsa yerin içinin (yeraltı boşluğunun / skybox'ın) göründüğü mikro çatlakların konum ve yönelimleri tespit edilmiştir:

1. **Görsel 1 (`media_1793962637954_6530db2e.jpg`):**
   - **Oyuncu Konumu:** $X = -158.0\,\text{m}$, $Y = 95.7\,\text{m}$, $Z = -65.0\,\text{m}$ (Biyom 9: Killi Çorak Topraklar).
   - **Hedef Blok:** Kırıktaş (Yoğunluk: 78).
   - **Çatlak Hattı:** Tam olarak $X = -160.0\,\text{m}$ ($cx = -10$ ile $cx = -9$ sınırı) ve $Z = -64.0\,\text{m}$ ($cz = -5$ ile $cz = -4$ sınırı) kesişim hattında, yamaç yüzeyinde tek bir üçgen/kuad boyutunda yırtık.
2. **Görsel 2 (`media_1793962653779_bc2c703a.jpg`):**
   - **Oyuncu Konumu:** $X = -174.0\,\text{m}$, $Y = 95.7\,\text{m}$, $Z = -63.0\,\text{m}$.
   - **Hedef Blok:** Toprak (Yoğunluk: 91).
   - **Çatlak Hattı:** Tam olarak $Z = -64.0\,\text{m}$ ($cz = -4$ ile $cz = -5$ sınır düzlemi) üzerinde, eğimli yüzeyde tekil bir kuadın düşmesiyle oluşan yeraltı sızıntısı.

> **Önemli Tespit:** Çatlaklar rastgele arazi noktalarında değil; **istisnasız $16\,\text{m}$'nin tam katı olan chunk sınırlarında ($X = -160\,\text{m}, Z = -64\,\text{m}$)** meydana gelmektedir. Bu durum hatanın bir arazi gürültüsü (noise) veya tepe noktası (vertex height) hatası olmadığını; **Chunklar Arası Surface Nets Komşuluk ve Çift Kenar Örme (Boundary Dual Edge Meshing)** mekanizmasından kaynaklandığını kesin olarak kanıtlamaktadır.

---

## 2. Literatür ve Benzer Voksel Oyunları Araştırması

İnternet kaynakları (Voxel Farm blogları - Miguel Cepero, Dual Contouring makalesi - Ju et al. 2002, Transvoxel algoritması - Eric Lengyel, Surface Nets makalesi - Sarah F. Frisken, GitHub `mikolalysenko/isosurface`, Reddit `/r/VoxelGameDev`, Astroneer ve Deep Rock Galactic geliştirici notları) taranarak voksel chunk sınırlarında çatlak oluşmasının evrensel nedenleri araştırılmıştır:

### 2.1. Primal (Marching Cubes) vs Dual (Surface Nets / Dual Contouring) Sınır Farkı
- **Marching Cubes (Primal):** Tepe noktaları küpün kenarları (edge) üzerindedir. Bir chunk $16\times16$ ise komşuyla bitişmek için $17\times17$ örneklem yeterlidir.
- **Surface Nets (Dual):** Tepe noktaları her bir $1\times1\times1$ hacimsel hücrenin (dual cell) **içine (merkezine)** yerleştirilir. Poligonlar ise hücrelerin ortak yüzeylerinden geçen kenarların etrafındaki 4 adet dual tepe noktasının birleştirilmesiyle (Dual Quad) örülür.
- **Evrensel Kural:** Bir sınır kenarını ($dx=16$ veya $dz=16$) çevreleyen 4 dual tepe noktası, hem mevcut chunk'ın hem de komşu chunk'ın içine düşer. Bu 4 tepe noktasının üretilebilmesi için her chunk'ın etrafında en az **1 voksel apron (pad = 18)** bulunmalı ve sınır kenarlarının çizimi için **8 komşunun tamamı (4 kardinal + 4 çapraz)** bellekte hazır olmalıdır.

### 2.2. Literatürdeki 3 Klasik Seam Hatası
1. **The Corner Gap (4-Chunk Köşe Boşluğu):** İki chunk yatayda komşudur ama çaprazlarındaki chunk henüz yüklenmemiştir. Sınır köşesindeki dual vertex hava ile birleştiği için $0.000\,\text{m}$ yerine kayar veya üretilmez.
2. **Asymmetric Quad Dropping (Asimetrik Kuad Düşürme):** Bir kenarı çevreleyen 4 dual hücreden biri `insideMask == 0` (tamamen hava) veya `insideMask == 255` (tamamen dolu) kaldığında `dualValid == false` olur. Eğer bir taraf kuadı çizerken diğer taraf `dualValid` eksikliği yüzünden kuadı iptal ederse o kenarda yırtık açılır.
3. **Single-Owner Edge Race Condition (Tek Sahip Kenar Yarışı):** Çift çizimi (double-sided overdraw) engellemek için batı ($X-$) ve güney ($Z-$) kenarlarını batı/güneydeki komşuya devreden algoritmada, eğer o komşu henüz sahnede yoksa kenar çizilmez; komşu sahneye gelince de kendisini çizip doğu/kuzey sınırını komşuya bıraktığını sanarak kenarı pas geçer.

---

## 3. Stratocraft Kod Tabanındaki 5 Somut Kök Neden

Kod tabanımız (`HybridSurfaceNetsAndShapeMesher.cs`, `Stage1InteractivePlayController.cs`, `Stage1MatterCrystalAndMiningController.cs`) satır satır incelenerek çatlakların nedenleri açığa çıkarılmıştır:

### Kök Neden 1: Dinamik Chunk Yüklemesinde Çapraz (Diagonal) Komşuların Kirletilmemesi (Eksik Dirty Bildirimi)
`Stage1InteractivePlayController.cs` içerisinde oyuncu haritada ilerlerken yeni bir chunk yüklendiğinde (`CreateAndRegisterDynamicContinentChunk`, satır 544–565):
```csharp
// MEVCUT KOD:
if (_worldChunks.ContainsKey((cx - 1, cz))) _dirtyChunks.Add((cx - 1, cz)); // Batı
if (_worldChunks.ContainsKey((cx + 1, cz))) _dirtyChunks.Add((cx + 1, cz)); // Doğu
if (_worldChunks.ContainsKey((cx, cz - 1))) _dirtyChunks.Add((cx, cz - 1)); // Güney
if (_worldChunks.ContainsKey((cx, cz + 1))) _dirtyChunks.Add((cx, cz + 1)); // Kuzey
```
- **Hata:** Yalnızca 4 kardinal komşu kirli (`_dirtyChunks`) işaretlenmektedir!
- **Sonuç:** Çapraz komşular (`cx-1, cz-1`, `cx+1, cz-1`, `cx-1, cz+1`, `cx+1, cz+1`) **asla kirli işaretlenmemektedir**.
- Oysa `HybridSurfaceNetsAndShapeMesher.cs` (satır 122–126) $18\times18$'lik apronu doldurmak için `chunkSW`, `chunkSE`, `chunkNW`, `chunkNE` dizilerine doğrudan bakar. Eğer çaprazdaki chunk daha önce komşusu yokken örülmüşse, köşe hücresinde (`dx=0, dz=0` veya `dx=16, dz=16`) yeni gelen chunk'ın verisi hesaba katılmamış olarak kalır ve yeniden örülmediği için $4$'lü köşe noktalarında mikro üçgen çatlakları açıkta kalır!

---

### Kök Neden 2: `dualValid` Kontrolünde 1 Hatalı Hücre Yüzünden Tüm Sınır Kuadının Düşmesi (Quad Dropping)
`HybridSurfaceNetsAndShapeMesher.cs` satır 285–330 arasında sınır kenarları için kuadlar yayılır:
```csharp
EmitDualQuadAsFacetedTriangles(dualVertices, dualMaterials, dualTemps, dualAo, dualValid, d0, d1, d2, d3, ...);
```
Ve `EmitDualQuadAsFacetedTriangles` fonksiyonu içinde:
```csharp
if (!dualValid[i0] || !dualValid[i1] || !dualValid[i2] || !dualValid[i3]) return;
```
- **Hata:** Kuadı oluşturan 4 dual hücreden tek biri bile (`d0, d1, d2, d3`) geçersizse (`dualValid == false`), fonksiyon sessizce `return;` etmekte ve kuadı çöpe atmaktadır!
- **Peki `dualValid` ne zaman `false` olur?**
  1. Hücrenin 8 köşesi de katıysa (`insideMask == 0xFF`) veya hepsi havaysa (`insideMask == 0`).
  2. Dilim atlama optimizasyonu (`sliceHasSolid[dy] == false`) sınır hücresini es geçtiğinde.
- Sınırda komşu chunk henüz belleğe tam oturmamışsa veya sınır vokselleri arasında ufak bir yoğunluk farkı varsa, 4 dual noktadan biri üretilemez; bunun sonucunda kenar kuadı tamamen kaybolur ve oyuncu o delikten yerin dibini görür!

---

### Kök Neden 3: Yüklenmemiş Komşularda Tek-Sahipli Kenar Asimetrisi (`clampUnloadedContinentEdges = false`)
`HybridSurfaceNetsAndShapeMesher.cs` satır 128 ve 272–275:
```csharp
bool clampUnloadedContinentEdges = false;
int startPxForXEdges = (chunkW != null || !clampUnloadedContinentEdges) ? (chunkW != null ? 1 : 0) : 1;
int endPxForXEdges = (chunkE != null || !clampUnloadedContinentEdges) ? 16 : 15;
```
- Devir #031'de kıta dış uçurum duvarlarını kapatmak için `clampUnloadedContinentEdges = false` yapılmıştır.
- Bu bayrak `false` olduğunda, eğer bir chunk batı komşusu (`chunkW == null`) henüz akıştan (streaming) gelmeden önce örülürse, `startPxForXEdges = 0` olur ve kenar kuadlarını havaya doğru üretir.
- Batı komşusu daha sonra yüklendiğinde (`chunkE != null` görerek `endPxForXEdges = 16` üretir), iki chunk arasında çift yüzey, çakışan ters normalli üçgenler veya komşu yeniden örülmediğinde tek taraflı boşluklar meydana gelir.

---

### Kök Neden 4: Kare Başına Örme Bütçesi Sınırlaması (Frame Throttling / Transient Seams)
`Stage1InteractivePlayController.cs` satır 1333:
```csharp
if (!forceImmediateAll && !isUrgentNearChunk && (processedSolid >= maxSolidPerFrame || _meshRebuildStopwatch.Elapsed.TotalMilliseconds >= 3.8))
{
    break;
}
```
- Kare süresini $3.8\,\text{ms}$ altında tutmak ve $60\,\text{FPS}$'yi korumak için tek karede en fazla 4 chunk örülmektedir.
- Oyuncu yürürken yeni bir chunk ($A$) yüklendiğinde, komşusu ($B$) kirli kuyruğuna alınır. Ancak $B$'nin yeniden örülmesi $1-3$ kare sonrasına kalabilir.
- Bu $1-3$ karelik geçiş penceresinde (transient window), $A$ yeni tepe noktalarıyla ekrandayken $B$ henüz eski örgüde kaldığı için oyuncunun baktığı kenarda anlık bir çatlak görünür.

---

### Kök Neden 5: Kazma/Koyma (CSG) Sırasında Çapraz Komşu Bildiriminin Bulunmaması
`Stage1MatterCrystalAndMiningController.cs` satır 1515–1518 ve `Stage1InteractivePlayController.cs` satır 211–214:
```csharp
if (lx <= 1 && chunks.ContainsKey((cx - 1, cz))) dirty.Add((cx - 1, cz));
if (lx >= 14 && chunks.ContainsKey((cx + 1, cz))) dirty.Add((cx + 1, cz));
if (lz <= 1 && chunks.ContainsKey((cx, cz - 1))) dirty.Add((cx, cz - 1));
if (lz >= 14 && chunks.ContainsKey((cx, cz + 1))) dirty.Add((cx, cz + 1));
```
- Bir oyuncu köşe kenarına yakın ($lx \le 1$ ve $lz \le 1$) bir blok kırdığında, hem Batı (`cx-1`) hem Güney (`cz-1`) komşu kirletilir; ancak köşe hücresinin doğrudan bağlı olduğu **Güneybatı (`cx-1, cz-1`)** komşusu kirletilmez. Bu da kazılan alanın köşesinde küçük bir çatlak bırakabilir.

---

## 4. Yeni Sohbet İçin Kesin Çözüm Planı (Action Plan)

Yeni sohbete geçildiğinde uygulanacak adımlar aşağıda netleştirilmiştir:

### Adım 1: 8 Komşulu Tam Komşuluk Bildirimi (3x3 Stencil Dirtying)
`Stage1InteractivePlayController.cs` içerisindeki `CreateAndRegisterDynamicContinentChunk` ve `MarkSubChunkDirtyInternal` fonksiyonlarında:
Sadece $4$ kardinal değil, **etraftaki tüm $8$ komşu ($dx \in [-1, 1], dz \in [-1, 1]$)** `_dirtyChunks` kuyruğuna eklenecektir.

### Adım 2: Çift-Kenar (Boundary) Dual Vertex Garantisi
`HybridSurfaceNetsAndShapeMesher.cs` içerisinde sınır kenarlarında bir kuad çizilirken eğer komşu dual vertex geçersizse (`dualValid == false`), kenarın açık kalarak yerin altını göstermesini engellemek için sınır yüzeyini kapatacak emniyetli üçgenleme (watertight boundary fallback) devreye sokulacaktır.

### Adım 3: Sınır Örme Önceliği (Boundary Mesh Co-Scheduling)
Bir chunk örüldüğünde sınırda bulunan komşuları kuyrukta en yüksek öncelikle (`Urgent`) örülecek, böylece $1-3$ karelik geçici açık kalma durumu ortadan kalkacaktır.

---

## 5. Taahhüt ve Güvence
Bu oturumda kullanıcı talimatı uyarınca **hiçbir kod değiştirilmemiştir**. Mevcut çalışma alanı testleri $46/46$ PASS durumdadır. Tüm bulgular yeni sohbette doğrudan uygulanmak üzere bu dokümanda ve `DURUM_VE_DEVIR.md` dosyasında kayıt altına alınmıştır.
