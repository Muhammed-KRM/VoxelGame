# Alt Ajan Araştırma Raporu: Padded Naive Surface Nets, Chunk Sınır Dikişleri ve 3B Hacim Araştırması

- **Tarih:** 2026-10-30
- **Alt Ajan ID:** 559e43b1-9fd9-4976-8532-2f53fd17880d
- **Ana Rapor:** [../FAZ1_FAZ2_SORUN_VE_COZUM_MIMARI_RAPORU.md](../FAZ1_FAZ2_SORUN_VE_COZUM_MIMARI_RAPORU.md)

---

# Stratocraft: Smooth/Faceted Voxel Arazi Arastirma Raporu

## 1. Referans oyunlar gercekte ne yapiyor?
- **Astroneer (System Era, UE4):** Gezegenler hacimsel yogunluk alani olarak saklaniyor. Deform araci mesh'e degil, bir yaricap icindeki yogunluk degerlerine ekleme/cikarma yapiyor; sonra sadece etkilenen chunk'lar yeniden mesh'leniyor. Arazi graph'i LLVM ile JIT (gelistirmede) veya AOT (release) derleniyor. Kesin poligonlastirma algoritmasi (MC mi, varyant mi) resmi olarak yayinlanmadi; topluluk MC-ailesi kabul ediyor. Kaynaklar: gamedeveloper.com Astroneer postmortem ve Zabir Hoque terrain-graph yazisi, unrealengine.com spotlight.
- **Deep Rock Galactic (Ghost Ship, UE4):** Resmi teknik dokuman cok az. Kaynaklar celisiyor: topluluk "voxel + MC" diyor, bazi Reddit/dev yorumlari "el yapimi oda sablonlari + prosedurel tunel + CSG oyma" diyor. **Bunu dogrulanmamis kabul et.** Fasetli gorunumun kaynagi kaba cozunurluk + flat/yari-flat normal + triplanar shader. Dogrulanmis tek ders bu.
- **7 Days to Die (Unity):** Her voxel'de `sbyte` density tutuluyor (modding API'si: `chunk.GetDensity/SetDensity`, `BlockChangeInfo.bChangeDensity`). Arazi MC-ailesi mesher ile cikariliyor (Assembly-CSharp'ta `MarchingCubes` sinifi). Bina blok/shape'leri **ayri sabit mesh'ler**, araziye karismiyor. Bilinen yan etki: arazinin zeminden fiskirmasi ve blok altinda bosluklar. Bunu blok yerlestirirken density'yi zorlayarak gizliyorlar (nexusmods modding notlari).
- **Enshrouded (Keen, Holistic Engine):** Arazi ve insa ayni voxel sisteminde. Bina malzemeleri voxel olarak yaziliyor, smooth mesh ile birlesiyor.

## 2. Chunk dikisleri (bug 1) – standart cozum ve index matematigi
Kural: her chunk kendi N³ voxel'inden **daha fazlasini** okur (padding), ama her kenari/quad'i **sadece bir chunk** uretir.

**N=16, Naive Surface Nets, flat normal:**
- Padded sample dizisi: `S = N + 2 = 18` (her yonde 1). Byte cinsinden 18³ = 5832 B.
- `local = global - O + 1`. O = chunk orijini (voxel). Indeks: `idx(x,y,z) = x + S*(y + S*z)`, x,y,z ∈ [0,17].
- Hucre (cell) gridi: `C = N + 1 = 17`. Hucre c'nin koseleri c..c+1 sample'lari. Hucre indeksi `cidx = x + C*(y + C*z)`, aralik [0,16].
- **Gecis 1 (vertex):** 17³ hucrenin hepsinde 8 kose maskesini hesapla. Kose biti `d >= 64` (IsoLevel 64). Mask 0 veya 255 degilse vertex = 12 kenardaki gecislerin ortalamasi. Interpolasyon `t = (64 - d0) / (float)(d1 - d0)`. `vertIndex[cidx]` doldur. En fazla 4913 vertex oldugu icin **UInt16 index** yeterli.
- **Gecis 2 (quad), sahiplik:** kenar (p → p+e_a) sadece baslangic sample'i p **kendi araligindaysa** (local 1..N, yani global O..O+N-1) uretilir. Boylece her kenar tek bir chunk'a ait olur.
  - Eksenler dongusel: a=X→(u=Y, v=Z), a=Y→(Z, X), a=Z→(X, Y).
  - Kenarda isaret degisiyorsa 4 hucre: `c0 = p - e_u - e_v`, `c1 = p - e_v`, `c2 = p`, `c3 = p - e_u`.
  - `d(p) >= iso` ise ucgenler (c0,c1,c2),(c0,c2,c3); degilse ters sira. Unity sol-el/CW sarim, bir kez test edip gerekirse cevir.
  - Referans verilen hucreler 0..N oldugu icin S=N+2 yeterli.
- **Paylasilan sinir vertex'leri:** chunk A'nin N. hucresi ile chunk B'nin 0. hucresi ayni global density'den hesaplanir, deterministik ayni sonucu verir. Float yuvarlama farkini sifirlamak icin local pozisyonu kuantize et: `pos = round(pos * 256) / 256`. Bu float'ta tam temsil edilir, T-crack kalmaz.
- Merkezi farkla **smooth gradyan normali** istenirse padding 2/2 olur (`S = N + 4`). Transvoxel'de godot_voxel `MIN_PADDING=1`, `MAX_PADDING=2` kullaniyor (`meshers/transvoxel/transvoxel.h`).
- **Padding nereden gelir:** 26 komsu chunk dizisinden satir satir kopya (Burst, `UnsafeUtility.MemCpy`). Komsu yuklu degilse global density fonksiyonu (worldgen) cagrilir. Asla "kendi verini tekrarla" yapma.
- **Bug 2 ("kagit arazi"):** dunya disindaki sample'lari tanimla:
  - Showcase/ada icin: dunya disi = **hava (d=0)** → yanlar ve taban kapanir, kapali kabuk olusur.
  - Sonsuz dunya icin: alt sinir (y<0) = kati, gorunmeyen taban uretilmez. Yatay sinirlarda gercek komsu veri.
  - Mevcut "kenar hucreleri atla" dongusu tamamen kaldirilmali.

## 3. Fasetli low-poly gorunum
- **A) Paylasilmayan vertex'ler (her ucgen 3 vertex + yuz normali):** vertex sayisi yaklasik 4-6 kat artar (SN'de bir vertex ~4 quad paylasir, quad basi 6 vertex olur). Bellek ve upload maliyeti buyur, ama her GPU'da kusursuz.
- **B) Shader'da ddx/ddy:** `n = normalize(cross(ddy(posWS), ddx(posWS)))`. Mesh paylasimli ve kucuk kalir, collider ayni mesh'i kullanir. Maliyet piksel basi ihmal edilebilir. Riskler:
  - URP'de render-target flip yuzunden isaret ters olabilir, bir kez dogrula.
  - Cok uzak veya kucuk ucgenlerde hafif 2x2 quad artefakti.
  - Shader Graph: Position(World) → DDX/DDY → Cross → Normalize → Normal(World, fragment).
- **Oneri:** B'yi Color32 UV-free UberShader'a ekle; sorun cikarsa A'ya dus. Quad bolme kosegenini tutarli sec (kisa kosegen) ki facetler duzenli gorunsun.

## 4. LOD dikisleri (sonraya)
- **Transvoxel** (Lengyel, MC tabanli, gecis hucreleri). Tablolar **MIT**: github.com/EricLengyel/Transvoxel, transvoxel.org.
- Surface Nets icin alternatifler:
  - **skirt:** sinir kenarindan asagi/iceri uzanan kisa duvar. En basit yol.
  - Sinirda dusuk LOD komsuyla ortak vertex'lere snap.
  - Vertex-shader morph: godot_voxel `CUSTOM0` + `u_transition_mask` yaklasimi (docs: `smooth_terrain.md`).
- Simdilik 1 LOD + skirt-hazir veri yapisi yeterli.

## 5. Acik kaynak referanslar (GitHub API ile dogrulandi)
| Repo | Lisans | Algoritma | Burst | Son push | Not |
|---|---|---|---|---|---|
| github.com/bigos91/fastNaiveSurfaceNets | MIT | Naive SN | Evet + SSE intrinsics | 2022-06 | 32³ chunk'ta ~0.1-0.4 ms. Sadece 32³ sabit, iso=0, sbyte. Kose maskesi SIMD fikri ve `SetVertexBufferData` kullanimi icin cok iyi |
| github.com/bigos91/fastMarchingCubes | (README'de baglanti) | MC | Evet | – | Ayni yazar |
| github.com/Eldemarkki/Marching-Cubes-Terrain | MIT | MC | Evet (Jobs+Burst) | arsivlendi, 2026-02 | Chunk/duzenleme/job mimarisi icin en iyi Unity ornegi, 601★ |
| github.com/Scrawk/Marching-Cubes | MIT | MC + Marching Tetrahedra | Hayir (managed) | 2023-07 | Ogretici |
| github.com/Tuntenfisch/Voxels | MIT | GPU Dual Contouring (compute) | GPU | 2022-08 | Keskin kenar ve DC referansi |
| github.com/bbQsauce5/transvoxel-unity | MIT | Transvoxel + octree LOD | Evet | 2025-01 | LOD asamasi icin |
| github.com/BinaryConstruct/Transvoxel-XNA | **Lisans yok** | Transvoxel | Hayir | 2019 | Kod kopyalama, sadece oku |
| github.com/Zylann/godot_voxel | MIT | Transvoxel/Blocky/Cubes | C++ | 2026-10 | En iyi teori dokumani, padding sabitleri |
| github.com/Phyronnaz/VoxelPlugin (Legacy) | Lisans alani yok | UE | – | 2026-06 | Sadece fikir |
| github.com/mikolalysenko/isosurface | MIT | SN/MC/MT (JS) | – | 2014 | Kanonik SN |
| "nickmcdonald/bonsai" | **bulunamadi (404)** | – | – | – | – |

Teori kaynaklari:
- 0fps.net/2012/07/12/smooth-voxel-terrain-part-2/ (Lysenko SN)
- boristhebrave.com/2018/04/15/dual-contouring-tutorial/
- github.com/TomaszFoster/NaiveSurfaceNets (bigos91'in baglanti mantigini aldigi kaynak)

## 6. Puruzsuz arazi + mimari shape birlesimi (catlaksiz)
- **7DTD modeli (onerilen hibrit):** shape'ler (rampa, kolon, kemer) **ayri sabit mesh**. Yerlestirme aninda:
  1. Shape'in kapladigi hucrelerde density'yi havaya cek (`d < 64`) ki arazi icinden fiskirmasin.
  2. Shape tabaninin hemen altindaki voxel'leri kati yap (`d = 127`).
  3. Shape mesh'ine 0.1-0.25 m araziye gomulen bir "temel flansi/skirt" ekle. Catlak **birebir eslesmeyle degil, ortusmeyle** gizlenir.
  Catlaksiz, ucuz ve keskin kenarlar korunur.
- **SDF'e CSG yazmak** (`d = max(d, shapeSDF)`): tamamen kaynasir, ama 1 m Naive SN keskin koseleri yuvarlar, kemer/kolon erir. Keskin kenar icin Dual Contouring (QEF) gerekir, karmasik. Sadece "toprak rampa/duzlestirme" gibi organik araclar icin kullan.
- **Enshrouded:** her sey ince voxel. 1 m cozunurlukte uygun degil.

## 7. Unity 6 / URP 17 pipeline (sifir GC)
1. **Veri:** sub-chunk basina `NativeArray<uint>` 4096 (32-bit voxel, 7-bit density). Kalici allocator, havuzlanmis.
2. **Job A (Burst `IJob`):** 27 chunk'tan 18³ padded `byte` density topla. Eksikse worldgen fonksiyonu cagrilir.
3. **Job B (Burst `IJob`):** SN gecis 1 ve 2. Cikti dogrudan `Mesh.MeshData`'ya:
   - `Mesh.AllocateWritableMeshData(k)` ana thread'de; `SetVertexBufferParams`/`SetIndexBufferParams` job icinde cagrilabilir.
   - Kompakt format: Position Float32x3 (veya Float16x4), malzeme Color32/UNorm8x4 (UV yok). Normal yok (ddx yolu) veya SNorm8x4. Index UInt16.
   - Gecici `NativeList`'ler `Allocator.Persistent` ve job basina `Clear()`.
4. **Ana thread:** `Mesh.ApplyAndDisposeWritableMeshData(array, meshes, DontValidateIndices | DontNotifyMeshUsers | DontRecalculateBounds)`. Bounds'u elle ver (16 m kutu). Bir karede k mesh toplu uygula.
5. **Collider (PhysX):**
   - `IJobParallelFor` icinde `Physics.BakeMesh(meshID, false, cookingOptions)`. **Burst degil**, ama thread-safe, worker'da calisir.
   - Sonra ana thread'de `meshCollider.cookingOptions` ayni deger olacak sekilde `sharedMesh` ata; aksi halde yeniden cook edilir.
   - Unity 6.x'te ID parametresi EntityId overload'u olabilir, derlemede kontrol et.
   - Docs: docs.unity3d.com/ScriptReference/Physics.BakeMesh.html, docs.unity3d.com/ScriptReference/Mesh.AllocateWritableMeshData.html
6. **DOTS Unity Physics kullanilirsa:** `Unity.Physics.MeshCollider.Create(NativeArray<float3>, NativeArray<int3>, filter)` Burst job icinde cagrilabilir, BlobAsset doner.
7. **Duzenleme (kazma):** degisen voxel sinirdaysa (local 0 veya N-1), padding'i etkiledigi komsu chunk'lari da dirty isaretle (en fazla 7 komsu).

## 8. Oneri: yamamak mi, yeniden yazmak mi?
**Cekirdegi yeniden yaz, algoritmayi koru.** Yani Naive Surface Nets + ddx/ddy (veya unshared) flat normal, Burst job olarak. Gerekce:
- Mevcut kod managed `List` + `System.Numerics` + editor script. Burst/zero-GC'ye "yamamak" zaten tam yeniden yazim demek.
- Naive SN, MC'ye gore:
  - Tablo gerektirmez (sadece kose maskesi).
  - Daha az ucgen uretir.
  - Quad yapisi duzgun facet verir.
  - 7-bit integer density ile deterministik calisir, sinir vertex'leri birebir esitlenebilir.
  - Ilerde DC'ye (QEF ile vertex konumu) yukseltmek ayni iskelette mumkun.
- MC (Eldemarkki) veya Transvoxel ancak LOD asamasinda tartisilmali.

**Somut adimlar:**
1. **Hizli showcase fix (1 saat):** padding S=18; kenar-atlama dongusunu kaldir; dunya disi=hava; sahiplik kurali p ∈ [1..N].
2. `Stratocraft.Voxel.Meshing` asmdef'inde Burst `SurfaceNetsJob` yaz (index matematigi yukarida). bigos91'den kose maskesi ve vertex API fikirleri (MIT, atif ver), Eldemarkki'den chunk/edit mimarisi (MIT).
3. Pozisyonu 1/256'ya kuantize et, UInt16 index kullan, bounds'u elle ver.
4. URP UberShader'a ddx/ddy flat normal ekle ve isaret testini yap.
5. `Physics.BakeMesh` job + `cookingOptions` eslesmesi; GC Alloc'un 0 B/kare oldugunu Profiler ile dogrula.
6. Shape tekerlegi: ayri mesh + density footprint + gomulu flans.
7. LOD: sonra skirt veya Transvoxel (tablolar MIT).
</SYSTEM_MESSAGE>
