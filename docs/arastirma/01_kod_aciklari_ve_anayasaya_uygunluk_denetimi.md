# Alt Ajan Araştırma Raporu: Faz 1 & Faz 2 Kod Tabanı Açıkları ve Anayasa Uyumsuzluk Denetimi

- **Tarih:** 2026-10-30
- **Alt Ajan ID:** ea707f37-b8d1-42da-8e3f-ec022d0f659f
- **Ana Rapor:** [../FAZ1_FAZ2_SORUN_VE_COZUM_MIMARI_RAPORU.md](../FAZ1_FAZ2_SORUN_VE_COZUM_MIMARI_RAPORU.md)

---

# Stratocraft Faz 1/2 GAP Denetimi (salt okunur)

**Genel tespit:** `Assets/_Stratocraft` altında **hiçbir DOTS/ECS sistemi yok**. Tüm "System/Job" sınıfları düz C# `sealed class` / `static class`. `Unity.Entities` yalnızca `Networking/ServerBootstrapAndLoopback.cs:5` içinde import ediliyor. Kodda hiç `[BurstCompile]`, `IJob`, `ISystem` ya da `NativeArray` yok. Oynanabilir sahne yalnızca iki dosya üzerinden çalışıyor: `Editor/StratocraftStage1SceneBuilder.cs` (editörde mesh'leri baker) ve MonoBehaviour `Character/Stage1InteractivePlayController.cs`. Bunlar Faz 1/2 logic sınıflarının **hiçbirini çağırmıyor**. Kullandıkları tek şey `VoxelBitUtility` ve `HybridSurfaceNetsAndShapeMesher`. Logic sınıflarını yalnızca testler çağırıyor.

## 1. Voxel Çekirdeği
| Mekanik | Anayasa/Doküman | Kodda durum | Sınıf | Önem |
|---|---|---|---|---|
| 32-bit bit düzeni | Bible 2.7.A (Mat8/Density7/Shape6/Rot5/Origin2/Dmg4), DevDoc 4.6.1 aynı | `Voxel/Data/VoxelBitUtility.cs:19-103` bible ile birebir aynı. `PackChiseled` 12-bit index'i doğru topluyor | A (data) | — |
| Chunk boyutu | Bible 2.7.A: 16³ sub-chunk, 16 KB | `VoxelChunkComponent.cs:8,51` 16×256×16 = 65.536 elemanlı **managed `uint[]`**, ECS bileşeni değil. `DirtySubChunkMask` 16 dilim tutuyor | B/E | Orta |
| MaterialID kataloğu | Bible 2.7.A: 1=Toprak, 2=Ham Taş, 14=Obsidyen, 28=Titanyum, 45=Kızıl Elmas | `Core/Constants/MaterialIdCatalog.cs:14-103`: 1=Stone, 2=Dirt, 6=Obsidyen, 36=Titanyum, 45=DarkMatter, 47=Gem. Sahne kurucu **üçüncü bir şema** kullanıyor: 1=Bazalt, 3=Çimen, 4-7=cevher, 8=Obsidyen, 9=Su, 10=Magma (`SceneBuilder.cs:303-334`, `1165-1174`). Bu değerler katalogda Sand/Bedrock/Obsidian/Basalt/Cobble/Gravel/Ice'a denk geliyor | E | **Kritik** |
| MicroVoxel 4096 havuzu (ShapeID 63) | Bible 2.7.A/B Kanal 2A | `MicroVoxelPool4096.cs` var ve testli. Ancak mesher 63'ü pahlı tam blok olarak çiziyor (`HybridSurfaceNets...cs:518`) | B | Orta |

## 2. Meshing / Render
| Mekanik | Anayasa | Kodda durum | Sınıf | Önem |
|---|---|---|---|---|
| Kanal 1: düzgün (blok olmayan) arazi | Bible 1.1, 2.7.B: Transvoxel/QEF DC, GPU compute, planar decimation | `HybridSurfaceNetsAndShapeMesher.cs:55-318` CPU Surface Nets ve jitter içeriyor. **Burst/zero-GC değil**: `System.Numerics`, `List<>`, her çağrıda `new Vector3[dualCount]` vb. (`:73-76`). QEF ve decimation yok | C | Yüksek |
| Chunk sınırı dikişi | Bible 2.7.B: seamless | Dual hücreler 0..14 (`:82-86`), üçgenleme 1..14 aralığında (`:224-228`). **Komşu chunk verisi (padding) okunmuyor**, bu yüzden x/z=0/15 ve y=0 sınırlarında yüz yok. Haç şeklindeki dikiş ve "kağıt gibi" alt yüzey buradan geliyor. Devir raporundaki `scale14To16` önerisi kök nedeni çözmez | E | **Kritik** |
| Alt/yan kapak | — | y=0 katı, altında hava yok, yani alt yüz hiç üretilmiyor. Yanlarda da yüz yok | E | Kritik |
| Kanal 2B: 62 şekil, `FullFaceMask` culling, 24+8 rotasyon | Bible 2.1/2.7.B | Yalnızca 2-7 arası 6 şekil var, geri kalan hepsi pahlı blok (`:486-523`). Rotasyon `rot&3` ile sadece 4 yaw (`:484`). Komşu yüz eleme yok | C | Yüksek |
| Kanal 2A Binary Greedy | Bible 2.7.B | `BinaryGreedyMeshingJob.cs` gerçek bitmask algoritması ama **ShapeID'ye bakmadan her şeyi küp çiziyor**. Yalnızca `ChunkMeshBuildSystem.cs:48` (bağlı değil) ve `GreedyMeshingBenchmarkTests` kullanıyor. Test eşiği 0.45 ms, doküman hedefi 0.15 ms | B | Düşük |
| Canlı düzenlemede yeniden meshleme | — | `Stage1InteractivePlayController.cs:333-393` her karede `new Mesh` yaratıyor (eskisi Destroy edilmiyor, GC ve sızıntı var). Ofset `coord.x*16` kullanılıyor, kurucu `(cx-1)*16` kullanıyor; düzenleme sonrası faset deseni kayıyor. Farklı palet (`ResolveFacetedDisplayColor`) var, UV2 yazılmıyor. Fırça x/z 1..14 ile sınırlı (`:229-234`) | E | Yüksek |
| Floating Origin | Bible 2.6.A | `ChunkSnappedFloatingOriginSystem.cs` sadece tuple güncelleyen bir logic sınıfı. Transform yok, sahnede yok | B | Orta |
| Chunk streaming | DevDoc F-07, 9×9 | `ChunkStreamingSystem.cs` native DLL'i çağırıyor (`:102`) ama sahne 2×2 sabit chunk ve sin tabanlı `SampleTerrainSurfaceY` kullanıyor (`SceneBuilder.cs:58-83, 210-233`). Persistence parametresi kullanılmıyor | B | Kritik |
| Render pipeline | Bible 16: URP | `GraphicsSettings.asset:49` ve QualitySettings'teki 6 seviyenin hepsinde `customRenderPipeline: {fileID: 0}`. Assets altında URP pipeline/renderer asset'i yok (yalnızca `UniversalRenderPipelineGlobalSettings.asset`). manifest'te `com.unity.burst "2.0.0"` sürümü şüpheli | E | **Kritik** |
| Uber Shader (Color32 R=MatID, G=Temp, B=Sharp, A=AO, MatCap, Biplanar, Rim) | Bible 16 | `StratocraftUberShader.shader/.hlsl` gerçekten var (MatCap `:58`, Biplanar `:69`, Rim `:194`) ama **sadece URP**, dolayısıyla SRP yokken çalışmıyor ve **hiçbir materyalde kullanılmıyor**. Kurucu `Shader.Find("Stratocraft/VertexColorUberLit")` çağırıyor (`SceneBuilder.cs:848`). **Aynı isimde iki shader var**: URP `StratocraftVertexColorUberLit.shader:1` ve Built-in `StratocraftVertexColorLit.shader:1`, hangisinin seçileceği belirsiz. Vertex rengine MatID yerine **önceden pişirilmiş palet RGB** yazılıyor (`SceneBuilder.cs:472, 1125`), MatID UV2'ye konuyor. Yani Color32 sözleşmesi bozuk | E | **Kritik** |

## 3. Dünya Üretimi
| Mekanik | Anayasa | Kodda durum | Sınıf | Önem |
|---|---|---|---|---|
| FastNoise2 SIMD | DevDoc 5.2.3 Adım 2 | `simd_world_noise.cpp:96-142` elle yazılmış AVX2 value noise (FastNoise2 değil). Sahnede kullanılmıyor | B/C | Yüksek |
| Y katmanları (0..-40 zindan, -40/-50 cevher, -80..-100 lav denizi, -105 manto) | Bible 2.x (satır 255-258) | Native y=0..255, y=0 Bedrock, deniz seviyesi 64 (`:23, 338-351`). **Negatif Y katmanları, lav denizi ve manto yok** | E | Yüksek |
| 14 cevher | Bible/DevDoc | Native 12 cevher + Kömür üretiyor (Boksit 33, Astral 41 yok, `:33-46`). `OreDistributionTable.cs:31-39` yalnızca 6 cevher içeriyor ve kullanılmıyor | C | Orta |
| Mağara (katener kubbe, sütun, pre-settling) | Bible 2.7.C.1 | Native hash'e dayalı rastgele boşluk (`:355-366`). Kurucuda tek, elle konmuş bir kubbe var (`SceneBuilder.cs:279-293`). Pre-settling yok | C | Orta |
| Native'de density | Bible 2.7.A | Her voxel Density=127 (`:58`). Gradyan olmadığı için Surface Nets basamaklı yüzey üretir | E | Yüksek |
| Halkalar | Bible: Lv1 200-1000 … Lv5 10000-15000, sınır 18.000 | Üç farklı şema var: native 200/1000/3000/6000/10000 (`:146-154`), `RingDifficultyCalculator.cs:12` 2048 m eşit aralık, `StratocraftConstants.cs:36-51` bir başkası | E | Orta |

## 4. Akışkan ve Yapısal Çökme
| Mekanik | Anayasa | Kodda durum | Sınıf | Önem |
|---|---|---|---|---|
| Sonlu akışkan CA | Bible 2.7.D | `FluidCellularAutomataJob.cs:81-176` hacmi koruyan CA (testli). Sahnede su ve magma **katı voxel** (9/10). Obsidyen, `wx<5.6` koşuluyla sahte biçimde yerleştiriliyor (`SceneBuilder.cs:307-311`) | B (sahnede C) | Yüksek |
| Stokiyometri | Bible: W≥128 ve L≥128 → 1 obsidyen | `:43-58` eşik doğru ama yalnızca 128'er birim tüketiyor, kalan sıvı yerinde duruyor. Oluşan blok ShapeID=1 küp | A/B | Düşük |
| Faz 1 Dijkstra | Bible 2.7.C: κ_k ağırlıklı, d_sup≥64, keystone | `GeodesicSupportDijkstraJob.cs`: birim ağırlık (+1), 32 bucket (`:19`), kemer/keystone yok. **Triaxial çapa fiilen kapalı**: `y==0 && hasBottomNeighbor` koşulu hiç sağlanamaz (`:214`). "Zero GC" iddiası var ama `new int[totalVoxels]` ayırıyor (`:48-49`). İndeks düzeni `x+sx*(y+sy*z)` chunk düzeninden farklı (`:228-230`) | C/E | Yüksek |
| Faz 2 Tributary DAG | Bible: α 8/4/2 ve E_u ağırlıklı, (y<<6)\|d_sup bucket, m_contents | `TributaryLoadDagJob.cs`: düz dikey birikim ve eşit yatay pay. Rijitlik/α yok | C | Orta |
| Malzeme tablosu | Bible: Toprak C_comp 80, Ahşap 400, Taş 1200, Tuğla 2500, Demir 12000, Titanyum 25000 (+m, C_shear, κ, E) | `StructuralMaterialProps.cs:42-56` tamamen farklı şema (kg, crush kg 28000/150000/950000, cantilever, Protodyakonov). Tuğla ve Titanyum yok | E | Yüksek |
| Terzaghi kubbesi | Bible: H = min(H_üst, ⌈1.25·d_sup⌉) | `:78-90` farklı formül: min(8, B/(2f)) | E | Orta |
| 1.5 s çatlak uyarısı, Rubble, 0.45 ms bütçe kuyruğu, 4-byte StructuralVoxelState | Bible 2.7.C | Grep sonucu boş, **hiçbiri yok** | D | Yüksek |
| Blok kırınca çökme | — | Sahne/controller `StructuralCollapseSystem` çağırmıyor | D (sahnede) | Kritik |

## 5. Madde Kristali ve Etkileşim
| Mekanik | Anayasa | Kodda durum | Sınıf | Önem |
|---|---|---|---|---|
| Vakum kapısı G_vac (Origin==0, cevher değil, Hardness≤L, y>-105, klan) | Bible 2.7.A | `MatterCrystalVacuumSystem.cs:41-105`: Origin ve cevher kontrolü var, `ERR_ORE_RESONANCE_IMMUNE` döndürüyor. **Sertlik kademesi, y>-105 ve klan kontrolü yok**. Tüm blok vakumluyor (yoğunluk fırçası yok) | B | Yüksek |
| Sahnedeki vakum | — | Controller cevherleri `mat 4..7` ile kodda sabit eliyor (`:251`). Kütle hiçbir yere aktarılmıyor (CrystalMassStorage yok), hata kodu yok | C | Yüksek |
| Kristalle yerleştirme Origin'i | Bible: daima 0. DevDoc 5.3.3: "OriginState=1 yapar" | Kod 0 yazıyor (`:139-145`, controller `:305, 320`), ama ShapeID=1 küp olarak | A/E | Orta |
| CrystalMassStorage 264 B fixed Q8[64] | Bible 3.3 tablo | `CrystalMassStorage.cs:13-29` managed class ve `Dictionary` | E | Orta |
| StackableItemSlot 16 B / UniqueEquipment 128 B | Bible 3.3 | `StackableItemSlot.cs:9` Size=8. `UniqueEquipmentInstance.cs:10-29` yaklaşık 48 B, 64 B'lık başlık yok | E | Orta |

## 6. Karakter
| Mekanik | Anayasa | Kodda durum | Sınıf | Önem |
|---|---|---|---|---|
| Hızlar 4/7/2/1 | Bible 3.1 | Sabitler doğru (`StratocraftConstants.cs:71-80`). `PhysicalCharacterControllerSystem.cs:17-23` 4/**6.8/2.2/1.1** kullanıyor. **Sahne controller'ı Walk 6.5 / Sprint 10.5** (`Stage1...cs:37-38`) | E | Yüksek |
| Fizik gövdesi | — | Controller CharacterController/Rigidbody kullanmıyor. Transform'u aşağı yönlü tek Raycast ile kaydırıyor (`:526-540`), duvarlardan geçiyor. ECS sistemi bağlı değil | C | Kritik |
| Kayma 3.66 m (8.2 m/s, sürtünme ODE, 15 EP) | Bible 3.5.D | 0.55 s × 14.2 m/s ≈ 7.8 m sabit (`:502-513`) | E | Orta |
| 3-ışınlı tırmanma 2.2 m, eğilme (0.9 m), sürünme (Z), yüzme, tırmanma, planör | Bible 3.1/3.5.D | Controller'da yok. Logic'te eğilme kapsülü 1.25 m (bible 0.90) | D | Yüksek |
| F5 FPS/TPS | Bible 3.1 | Yalnızca TPS orbit kamera (`:476-490`) | D | Orta |
| Oksijen BFS 512 + gökyüzü ışığıyla erken çıkış, O_max = 15·V | Bible 3.5.A | `EnclosedOxygenFloodFillSystem.cs:18-19` MaxNodes 2048, açık eşik 64, gökyüzü ışığı yok, chunk sınırı "açık" sayılıyor (`:77-83`), `HashSet` ve `Queue` kullanıyor. Farklı tükenme modeli | C/E | Orta |
| Termal ODE (W∈[0,1], λ_homeo) | Bible 3.5.B | `ThermalAndBiologySystem.cs:33-37` basit üstel yaklaşım, ıslaklık bool | C | Düşük |
| Stamina histerezisi %15 | Bible 3.5.B.3 | Yok; stamina 0'da sadece Standing'e düşüyor (`PhysicalCharacter...:172-184`) | D | Orta |
| Camera-Clutch / 1€ filtresi | Bible 4 | `CameraClutchController` ve `OneEuroFilter3D` var ama controller `Vector2.Lerp` kullanıyor (`:438`) | B | Düşük |

## 7. Envanter
| Mekanik | Anayasa | Kodda durum | Sınıf | Önem |
|---|---|---|---|---|
| 3B kumaş çanta 1.40×1.00, 6 bölge, instanced piramitler, −%70 hız | Bible 3.3 | Sahnede statik ölçekli bir pahlı blok ve elle yapılmış külçe meshleri. `I` tuşu yalnızca `SetActive` yapıyor, hız düşmüyor (`Stage1...:113-129`). `TacticalClothBackpackSystem` 36 slotluk grid (6 bölge değil). `BackpackInstancedRenderer` sadece veri üretiyor, hiçbir `Graphics.*` çizim çağrısı yok | C | Yüksek |
| Kılıflar 0.6/1.3/1.8, G ile anında bırakma | Bible 3.4 (bel 0.5/0.6, sırt 0.9/1.3) | Logic doğru (`BodyHolsterStateMachine.cs:24-27`). Controller her slot için 1.8 s kullanıyor, G sadece metin değiştiriyor (`:132-142`) | B/C | Orta |
| 64-bit versiyonlu CAS sandık | Bible 3.3 | `VisualChestCasSystem.cs:87-107` gerçek CAS, 8 thread testi var. Sahnede sandık yok | B | Orta |

## 8. Canlılar ve AI
| Mekanik | Anayasa | Kodda durum | Sınıf | Önem |
|---|---|---|---|---|
| 21 yaratık listesi | Bible 13.0 (Goblin, Yaban Domuzu, Direwolf, Ghoul, Savaş Atı, Ork …, Automaton, Void Stalker; 5/5/5/4/2; Lv5 7.5k-12k HP) | `Creature21BehaviorSystem.cs:54-86` **tamamen farklı isimler**, 4/4/4/5/4 dağılımı, HP 85-1650 | E | Yüksek |
| Davranış (gezinme, beslenme, sürü) | DevDoc 5.3.3 Adım 4 | Yok. Yalnızca katalog, uzuv cezası ve sütun vurma fonksiyonu var | D | Kritik |
| Hibrit LoD / NavSpan | Bible 1.5 | `HybridAiLodPromotionSystem` (histerezis) ve `OffScreenNavSpanStepJob` tek chunk'a sıkıştırılmış basit adım (`:45-50`). Bağlı değil | B | Orta |
| Kırılabilir voxel A* | Bible 13.3.A | Grep sonucu boş | D | Orta |
| Sahnedeki canlılar | — | 3 adet statik `mob_ash_stalker` (katalogda yok) ve bir Phoenix mesh'i. Animator/VAT/hareket yok (`SceneBuilder.cs:159-189`) | C | Kritik |

## 9. Asset Fabrikası ve Testler
| Mekanik | Anayasa | Kodda durum | Sınıf | Önem |
|---|---|---|---|---|
| Blender headless bpy ile üretim | Bible 16 | **Blender hiç çağrılmıyor.** `generator_path="PathA_BlenderBpy"` yalnızca bir etiket (`armor_15piece_generator.py:181`). Tüm modeller `_append_box_part` ile yapılmış, 3 halkalı 8 kenarlı prizmalardan oluşan kitbash (`base_generator.py:107-200`) | C | Yüksek |
| Varlık sayısı | DevDoc 5.3.3: 29 atölye, 14 klan, 75 batarya, 25 mayın, 21 yaratık, 13 boss | Manifest'te **9 kayıt** var. 32 glb'nin 15'i zırh, 1'i mob, 1'i boss, 5'i severed parça. Devir raporundaki "21 yaratık modeli" iddiası yanlış | C | Yüksek |
| 4 açılı PNG | Bible 16 | Her çağrıda `prefer_blender_cli=False` (`agent_generate_asset.py:106,112`), yani PIL ile yazılım çizimi. Blender dalı da tek görüntü render ediyor (`contact_sheet_renderer.py:115-128`) | C | Orta |
| asset_validator | Bible 16 | COLOR_0, boyut, pivot ve poly bütçesini kontrol ediyor. `auto_correct_glb_transform` ölçeği zorla düzeltiyor (hataları gizleyebilir) | B | Düşük |
| GLB import | — | Regex ile yazılmış parser ilk 4 bufferView'u varsayıyor ve UInt16 index kullanıyor (`SceneBuilder.cs:1035-1155`) | C | Yüksek |
| 24 NUnit testi | — | Hepsi izole logic testi (bit packing, pool, Morton, CAS, vakum, akışkan, çökme, FO, halka, oksijen, kılıf, NavSpan). **Sahne, MonoBehaviour, mesher, seam ya da render testi yok**. HybridSurfaceNets için test yok | B | Yüksek |

## Dokümanlar arası çelişkiler
1. **Bit düzeni:** DevDoc 5.2.3 (`:3065`) `[8b Mat | 6b Dmg | 4b Temp | 2b Origin | 12b Micro]` diyor. Bible 2.7.A ve DevDoc 4.6.1 (`:877`) farklı. DevDoc 5.2.3'te Origin=3 "KlanKalkani", Bible'da "VOXEL_LOCKED_ATOMIC".
2. **Obsidyen:** Bible W≥128 ve L≥128 diyor. DevDoc 5.2.1/5.2.3/5.2.4 "≥4 Lav + ≥1 Su" ve "Su 8 / Lav 4 seviye" diyor (kod 8-bit 0..255 kütle kullanıyor).
3. **Kristalle yerleştirme:** Bible "daima Origin=0" diyor, DevDoc 5.3.3 "OriginState=1 yapar" diyor.
4. **Halkalar:** DevDoc 5.2.1'e göre 5 halka 0-3000 m ve 3000-3600 m felaket kuşağı. Bible'a göre 5 halka 200-15000 m ve 15-18 km sınır bölgesi.
5. **Duruşlar:** DevDoc 5.3.3 eğilme 1.2 m, sürünme 0.6 m diyor. Bible eğilme için 0.90 m boşluk veriyor. Kod 1.25/0.65.
6. **Mesher:** DevDoc Faz 1 "Binary Greedy" diyor. Bible 2.7.B, Kanal 1 için Transvoxel/DC şart koşuyor (küp yasak).
7. **MaterialID:** Bible örnek ID'leri, MaterialIdCatalog ve SceneBuilder paleti birbirinden tamamen farklı.

## En kritik 10 açık
1. **SRP atanmamış.** URP pipeline asset yok. Uber shader'lar çalışmıyor, iki shader aynı ismi taşıyor.
2. **Color32 sözleşmesi bozuk.** Vertex rengi palet RGB, MatID değil. Uber shader (MatCap, Biplanar) hiç kullanılmıyor.
3. **Mesher padding okumuyor.** Chunk sınırlarında boşluk var, alt ve yan yüz yok (dikiş ve "kağıt zemin").
4. **Oynanabilir sahne logic katmanından tamamen kopuk.** Streaming, native gen, FO, akışkan, çökme, vakum, çanta, kılıf ve AI'nin hiçbiri sahnede çalışmıyor.
5. **ECS/Burst yok.** "System/Job" sınıfları managed ve GC yapıyor. 0 B/kare GC bütçesi ihlal ediliyor.
6. **Üç farklı MaterialID şeması.** Kod içinde ve bible ile tutarsız.
7. **Karakter hareketi raycast ile transform kaydırma.** Çarpışma yok, hızlar 6.5/10.5. Eğilme, sürünme, tırmanma, yüzme, F5 yok.
8. **Dünya üretimi bible'a uymuyor.** Negatif Y katmanları/lav denizi/manto yok, density 127 sabit, FastNoise2 yok. Sahne sin fonksiyonlu 32×32 m demo.
9. **Yapısal çökme bible'dan sapıyor.** Malzeme tablosu ve formüller farklı, triaxial çapa fiilen kapalı, 1.5 s uyarı ve rubble yok, blok kırmaya bağlı değil.
10. **Asset fabrikası:** Blender yok, prizma kitbash. 9 manifest kaydı, 21 yaratıktan 1, 13 boss'tan 1 model. Yaratıkların davranışı/animasyonu yok ve 21'lik liste bible ile uyuşmuyor.
</SYSTEM_MESSAGE>
