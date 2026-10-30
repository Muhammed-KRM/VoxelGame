# Alt Ajan Araştırma Raporu: İki Aşamalı Domino Yıkım Fiziği, Destek Grafı ve 16-Bit Hücresel Akışkanlar

- **Tarih:** 2026-10-30
- **Alt Ajan ID:** 1b7bb042-1e24-4547-8259-a7d74072c3dd
- **Ana Rapor:** [../FAZ1_FAZ2_SORUN_VE_COZUM_MIMARI_RAPORU.md](../FAZ1_FAZ2_SORUN_VE_COZUM_MIMARI_RAPORU.md)

---

# Stratocraft Faz 1 Araştırma Raporu: Yapısal Bütünlük, Sonlu Akışkanlar, Arazi Şekillendirme

> Not: search_web yönlendirme (redirect) linkleri döndürdü. Aşağıdaki URL'lerin çoğu resmî/kanonik adresler; "~" ile işaretlenenleri kullanmadan önce doğrulayın.

## 1. Oyunlar SI'yı nasıl yapıyor?

| Oyun | Yöntem | Bizim tasarımla örtüşen nokta |
|---|---|---|
| **7 Days to Die** | Blok başına `Mass` + `MaxLoad` ("glue", yüzey başına yatay kapasite) + ikili "vertical support" değeri var. Zemine kesintisiz inen dikey kolon **sınırsız** taşır. Yatay konsolda kümülatif kütle glue'yu geçince kopuyor (ahşap 40/5 = 8 blok). Decompile edilen sınıflar `StabilityCalculator` (zaman dilimli kuyruk) ve `StabilityViewer` (sarı→kırmızı→pembe overlay). Harmony modları (ör. *SteadyFrame*) stabilite hesabından kaynaklanan kare takılmalarını düzeltmeye çalışıyor, yani spike sorunu gerçek. | Phase 1 (destek mesafesi) + Phase 2 (yük toplama). "Yükseltme sırası tuzağı": hafif çerçeve ağırla değiştirilince toptan çöküyor. Bunu 1.5 s'lik çatlama uyarısı ve UI rengiyle hafifletin. |
| **Valheim** (`WearNTear`) | Zemine değen parça MaxSupport alıyor (ahşap 100, taş 1000, demir 1500). Her bağlantıda `S_this = min(Max_this, S_other − loss·(dist+0.1)·S_other)`, dikey ve yatay loss ayrı (taş yatay = 1.0, yani konsol yapamaz). MinSupport altına düşen parça kırılıp kaskadı başlatıyor. | Bizim d_sup + malzeme başına yatay ceza ile **birebir aynı fikir**. Oyuncular tarafından iyi anlaşılan, kanıtlanmış bir model. |
| **Teardown** | Stres hesabı yok, yalnızca **bağlantı** var: hasar noktasından yerel flood-fill. Statik köke bağlı olmayan ada yeni dinamik gövde olur. Yapı tek bir voxel ile bağlı kaldığı sürece ayakta durur (bilinçli oynanış tercihi). Yeni motorda 8³ seyrek brick + bitmap var. | CCL ile kopan adaları bulma ve yalnızca yerel ROI'de hesap. |
| **Space Engineers** | Grid bir graf; blok silinince BFS ile `Split()` yapılıyor, voxel'e bağlı kısım statik, kopan kısım dinamik oluyor. Yük hesabı yok. | CCL + statik/dinamik geçişi. |
| **Medieval Engineers** | Keen, Havok Destruction'ı denedikten sonra kendi SI algoritmasını yazmış. Kütle/dayanım ve SI görselleştirme modu var (blog.marekrosa.org). | SI overlay'i debug aracı olarak şart. |
| **Red Faction Guerrilla (Geo-Mod 2.0)** | Önceden kırılmış parçalar ve gerçek zamanlı stres grafı: aşağı doğru ağırlık, yukarı doğru dayanım. Eşik aşılınca parça dinamiğe geçiyor. GDC 2011, Eric Arnold, *"Living in a Stressful World: Real-time Stress Calculation for Destroyable Environments"* (GDC Vault). Sanatçılar yapısal mantıkla modellemek zorunda kalmış, yoksa level yüklenirken bina çöküyordu. | Phase 2'nin AAA kanıtı. **Uyarı:** worldgen yapıları "load-time collapse" testinden geçmeli. |
| **Enshrouded** | SI **yok**, yaratıcı özgürlük tercihi. | Doğal, dokunulmamış arazinin çökmemesi bizim "triaxial confinement" kuralıyla sağlanıyor. |
| **Whiting/Ochsendorf/Durand 2009** (SIGGRAPH Asia, ACM TOG 28(5), ~doi 10.1145/1618452.1618458) | Rijit blok dengesi, yalnız-basınç ve sürtünme koşulu, lower-bound limit analizi (LP). | Gerçek zamanlı için ağır. Kemer/kilit taşı indirimini **sezgisel** tutun, LP kullanmayın. |

**Bilinen tuzaklar:** (1) Flood-fill uzak bir ankraj ararken patlayabilir. Mutlaka mesafe sınırı (d_sup ≥ 64 → kes) ve ROI kullanın. (2) Zincirleme kaskad aynı karede çözülürse spike olur, dalga halinde amortize edin. (3) Yatay yük dağıtımında döngü/osilasyon çıkabilir. (4) Upgrade veya yerleştirme anında beklenmedik toptan çöküş. (5) Worldgen yapılarının yükleme anında çökmesi.

## 2. Algoritmalar ve Burst ipuçları

**Phase 1 (Dial / bucket queue):** Kenar ağırlıkları küçük tamsayılar: dikey 1, yatay malzeme cezası 2..8, kemer indirimi −1. d_sup tavanı 64 olduğu için **64+W_max boyutunda dairesel bucket dizisi** yeterli, O(E+W·V) (https://en.wikipedia.org/wiki/Bucket_queue). Burst'te her bucket için `NativeList<int>`, ya da tek bir `NativeArray<int>` havuzu ve bucket başı/sonu ofsetleri. "Visited/settled" için `NativeBitArray`.

**En kritik optimizasyon:** Doğal, dokunulmamış ve 4 yatay komşusu dolu (triaxial) voxel'leri **d=0 ankraj** sayın. Böylece graf yalnızca oyuncu yapıları ve kazılmış yüzey kabuğundan oluşur ve düğüm sayısı binler seviyesinde kalır.

**Artımlı güncelleme (silme):** Minecraft/Seed of Andromeda ışık silme algoritmasının birebir uyarlaması (iki kuyruk):
- *Removal BFS:* silinen hücreden başla. Komşunun d'si, bu hücreden türetilmiş olabilecek değerdeyse (`d_n == d_cur + w`) INF yap ve kuyruğa ekle. Değilse komşu "sınır" olur ve add-queue'ya gider.
- *Repair:* sınır hücrelerden Dial'ı yeniden çalıştır. Sonunda INF kalanlar desteksizdir; d ≥ 64 olanlar da çöker.
- Ekleme daha basit: yeni hücreden düz Dial relax.
- Tam yeniden hesap yalnızca chunk yüklenirken ve doğrulama testlerinde.

**Phase 2 iyileştirmesi:** Y-sıralı DAG yerine **d_sup azalan sırada** işleyin. Kenarları yalnızca d'si kesin daha küçük destekçilere verin; böylece graf **matematiksel olarak döngüsüzdür** ve sıralama Phase 1 bucket'larından bedavaya gelir. Yük, bu destekçilere rijitlik (stiffness) oranında dağıtılır. Basınç kapasitesi (80/400/1200/2500/12000/25000) ve kesme limiti burada kontrol edilir.

**CCL:** Silmeden sonra INF kalan hücreler üzerinde etiketli BFS (`NativeQueue<int>` + `NativeArray<int> label`). Union-find yalnızca büyük toplu patlamalarda paralel blok-CCL için gerekli.

**Amortizasyon:** Edit olayları `NativeQueue<EditEvent>`'e girer. Her karede bir `IJob` (Burst) en fazla N düğüm işler; kalan iş bir sonraki kareye devreder (7DTD'nin zaman dilimli yaklaşımı). 1.5 s'lik çatlama uyarısı ≈ 90 karelik bütçe demek: uyarı sırasında Phase 2 ve CCL'yi bitirin, çöküşü uyarı bitince uygulayın. Kaskadda her "dalga" bir sonraki karede işlensin, dalga başına düğüm sınırı koyun.

**Burst notu:** Bulunan örnek kodlar job içinde `new int3[]` kullanıyor; bu Burst'te **geçersiz** (managed alloc). 6 komşuyu açık açık yazın (unroll) ya da `FixedList` kullanın. Persistent kuyrukları yeniden kullanın, kare başına allocation yapmayın (0 B GC hedefi).

## 3. Düşen enkaz

- **Greedy box merge:** X→Y→Z yönünde maksimal kuboid büyütme. Önce en büyük hacmi çıkarın. Sonuç 12 kutuyu aşarsa: (a) küçük kutuları komşu kutunun AABB'sine yutun (biraz taşma kabul), (b) iç hacmi 1–2 büyük kutuyla, kabuğu greedy ile temsil edin, (c) gerekirse kümeyi 2 gövdeye bölün.
- **Unity Physics (DOTS):** `CompoundCollider.Create(NativeArray<ColliderBlobInstance>)`, ardından alt blob'ları Dispose edin. `PhysicsMass.CreateDynamic(blob.Value.MassProperties, kütle)`. Spawn'u ECB ile physics step dışında yapın. ≤12 kutulu compound ve tek primitif debris için DOTS çok ölçeklenebilir. Sık **değişen** büyük compound'lar ise pahalı; bizde değişmiyor (spawn edilip uyuyunca yeniden voxelleştiriliyor), yani uygun. DOTS stack kullanıldığı için Unity Physics öneriyorum. Havok for Unity yedek seçenek.
- **Arazi collider'ı:** Unity Physics `MeshCollider.Create` job içinde çalışabiliyor. PhysX tarafında `Physics.BakeMesh(meshId, false, options)` job'da thread-safe; cooking options MeshCollider'daki ile **aynı** olmalı, yoksa ana thread'de tekrar pişirir (https://docs.unity3d.com/ScriptReference/Physics.BakeMesh.html).
- **Yeniden voxelleştirme:** Gövde 1.5 s uyuyunca (`linear+angular velocity < ε`), hedef grid hücre merkezlerini ters transformla küme lokal grid'ine örnekleyin (nearest). Kütleyi korumak için hücre sayısını orijinale göre ayarlayın. Malzeme "gevşek moloz" olsun (düşük dayanım); moloz Phase 1'de ankraj sayılmaz.

## 4. Sonlu akışkanlar

- **7DTD A21:** Su artık blok değil, ayrı "water voxel" katmanı ve kütle değeri; simülasyon native/Burst job'larda (`WaterSimulationNative`, modlama kaynakları). Şelale ve sürekli akış performans için bilinçli olarak sınırlandırılmış, sonsuz su exploit'i kaldırılmış.
- **jgallant LiquidSimulator** (https://github.com/jongallant/LiquidSimulator, blog: jgallant.com/2d-liquid-simulator-with-cellular-automaton-in-unity/): sırasıyla aşağı → sol/sağ → yukarı akış. Hücreler "MaxCompression" ile hafifçe sıkışabiliyor, böylece U-tüp oluşuyor. "Settle" sayacı ile uyuma var. 3D'ye taşımak kolay.
- **Dwarf Fortress** (dwarffortresswiki.org/index.php/Pressure): basınç kalıcı bir alan değil, **davranış**. Dolu bir hücreye yukarıdan akış gelince, dolu hücreler boyunca BFS ile kaynak Z'sinden **aşağıdaki** ilk boş hücre bulunur ve su oraya "ışınlanır". Çapraz geçiş basıncı keser.
- **Noita** (GDC 2019, https://www.youtube.com/watch?v=prXuyMCgbTc): 64×64 chunk, chunk başına dirty-rect, 4 geçişli checkerboard çok-thread, 32 px güvenlik marjı, aşağıdan yukarı tarama.
- **Terraria:** aktif sıvı listesi, settle geçişi, çok ince sıvının yok edilmesi.
- **Sandspiel** (https://github.com/MaxBittker/sandspiel, MIT): Rust/Wasm CA ve render ayrımı. **Frager/CA-Fluid-Simulation** (https://github.com/Frager/CA-Fluid-Simulation): Unity'de 3D CA, compute shader + Marching Cubes yüzeyi.

**Bizim 2 byte'lık hücre için önerilen tick:**
1. **Dikey:** `move = min(level, 255 − below)`, tamsayı.
2. **Yatay eşitleme:** 3D'de chunk içinde 2×2 (x,z parite) red-black. Her hücre 4 komşusuyla `Δ = (a−b) >> 2` alışverişi yapar. **Tamsayı + çift taraflı yazım** sayesinde kütle korunumu birebir sağlanır; testte toplam seviye her tick sabit olmalı.
3. **Basınç/U-tüp:** DF tarzı ışınlama BFS'i (sınırlı adım, ör. 256). Sıkıştırma jitter'ından daha kararlı.
4. **Uyku:** |Δ| < 2 durumu N=8 tick sürerse settled bit'i set edilir, hücre 0 CPU harcar. Komşu edit, terrain edit veya çöküş olunca wake. Chunk düzeyinde aktif set + dirty AABB (Noita tarzı). Paralel job'larda 8 geçişli 2×2×2 chunk checkerboard.
5. **Su+lav:** temas anında one-shot reaksiyon (her ikisi ≥128 → 1 obsidyen bloğu, ikisi de tüketilir; değilse buhar/kül), event kuyruğuna.

**Render:** settled su için kolon başına heightfield üst yüzeyi (ucuz). Akan su için level/255 → yoğunluk alanına Surface Nets uygulayıp terrain SDF ile kırpın: `fluidSDF = max(fluidSDF, −terrainSDF)`. Kıyı yumuşaklığı vertex'lerde trilinear örnekleme ve shader'da derinlik farkıyla köpük.

## 5. Astroneer/DRG tarzı arazi şekillendirme

- **Yoğunluk/SDF alanı + CSG:** kazma `d' = max(d, −sphere)`, yumuşak sürümü `d' = d − k·falloff(r/R)·dt`, smoothstep falloff. Malzeme ID'si yoğunluktan **ayrı** saklanır; ekleme modunda kristal içeriğinden atanır. **Damar koruması:** brush, ore maskeli voxel'leri atlar.
- Astroneer'de flatten modu ilk vuruş düzlemine SDF projeksiyonu yapıyor. Multiplayer'da voxel değil, **komut** (merkez, yarıçap, mod) senkronize ediliyor. Teardown da aynı yöntemi kullanıyor.
- **DRG:** chunk grid + MC, değişen chunk'lar dirty işaretlenip yerelde yeniden mesh'leniyor, ardından collider/nav güncelleniyor.
- **Uygulama:** etkilenen chunk'lar = brush AABB + 1 voxel apron (normal/seam için). Remesh kuyruğu kameraya uzaklığa göre öncelikli, kare başına K chunk. Collider mesh'ten sonra job'da pişirilir. Edit bitince aynı ROI üzerinden SI-Phase1 edit event'i ve akışkan wake tetiklenir.
- **Referanslar:** https://github.com/Eldemarkki/Marching-Cubes-Terrain (MIT, Jobs+Burst terraform), https://github.com/SebLague/Terraforming (MIT).

## 6. Açık kaynak referanslar
- jongallant/LiquidSimulator (CA su, Unity C#)
- Frager/CA-Fluid-Simulation (3D CA + MC)
- MaxBittker/sandspiel (MIT)
- Eldemarkki/Marching-Cubes-Terrain (MIT, Burst)
- SebLague/Terraforming (MIT)
- ~vox3D/vox3D (MIT, Box3D fork, voxel stres/kırılma; doğrulayın)
- fenomas/voxel-physics-engine (MIT, JS)
- Ben Arnold / Seed of Andromeda "Fast Flood Fill Lighting" (iki kuyruklu silme algoritması)
- 0fps.net (meshing makaleleri)

Hazır **voxel SI + Burst** açık kaynak projesi bulamadım. Bu kısmı kendimiz yazmamız gerekecek.

## 7. Öneri ve uygulama sırası

**Karar:** İki fazlı tasarımı **koruyun**, ama kademeli devreye alın. Phase 1 tek başına Valheim/7DTD seviyesinde oynanış sağlar. Phase 2 (ezilme) üzerine eklenen bir katman olmalı ve bir özellik bayrağıyla açılıp kapanabilmeli. Terzaghi çökme mekanizmasını ana çözücüye **gömmeyin**, ayrı bir "kazı olayı" kuralı yapın: yeni boşluğun açıklığı B, malzeme limitini aşarsa tavanın üstündeki `Hp = k·B` yüksekliğindeki kubbeyi (taş k≈0.25, toprak k≈1.1; Terzaghi sınıf III ve VI aralıkları) "gevşek" işaretleyin, uyarıdan sonra CCL kümesi olarak düşürün.

**Sıra:**
1. Veri: malzeme tablosu (ağırlık, basınç, kesme, yatay ceza, rijitlik) ScriptableObject/Blob olarak; SI overlay (renk = d_sup/64 ve yük/kapasite).
2. Phase 1 tam hesap (Burst Dial) + triaxial ankraj kuralı + birim testleri.
3. Artımlı silme/ekleme (iki kuyruk) + tam hesapla **eşitlik testi** (rastgele 1000 edit sonrası d_sup dizileri birebir aynı olmalı).
4. CCL → falling cluster → greedy box (≤12) → Unity Physics spawn → uyku → yeniden voxelleştirme.
5. 1.5 s çatlama uyarısı (VFX/ses) ve kare bütçeli kuyruk; kaskad dalgaları.
6. Phase 2 yük DAG'ı (d azalan sıra) + ezilme ve kesme.
7. Terzaghi kazı kuralı.
8. Akışkan: dikey, ardından yatay red-black, uyku, chunk aktif set, DF basınç BFS'i, su+lav reaksiyonu, yüzey render.
9. Matter Crystal brush'ı (SDF CSG, ore maskesi), ardından dirty chunk remesh ve collider job; SI ve akışkan wake entegrasyonu.

**Test senaryoları (EditMode + PlayMode, profiler assert'li):**
- **20 m kule, 4 sütun:** 1 sütun silinince ayakta (yük yeniden dağılır), 3 sütun silinince 1.5 s uyarı sonra çöküş, küme ≤12 kutu, yerel edit < 0.25 ms.
- **Konsol limiti:** ahşap/taş/demir için d_sup ≥ 64 eşiğinde tam olarak N'inci blokta kopma.
- **Kemer:** kilit taşlı kemer ayakta kalır, kilit taşı silinince çöker.
- **Ezilme:** toprak sütun üstüne taş kütle konunca ezilir, titanyum sütun dayanır.
- **Dağ:** dokunulmamış dağın tabanına 1 blokluk tünel açılır, çökme olmaz. Tünel genişletilip B limiti aşılınca kubbe çöker.
- **Kaskad stres:** 64×64'lük köprü ortadan kesilir, kare süresi 0.45 ms amortize sınırını aşmaz, spike olmaz.
- **Akışkan:** kapalı kapta 10.000 tick sonra toplam kütle birebir aynı; U-tüpte iki kol eşit seviyede; settled göl için CPU ≈ 0; su(≥128)+lav(≥128) → tam 1 obsidyen bloğu.
- **Terraform:** ore damarı brush ile kazılamaz; brush sonrası remesh + collider ≤ 2 kare; GC Alloc 0 B.
- **Yeniden voxelleştirme:** molozun hücre sayısı orijinalin ±%10'u içinde ve moloz ankraj sayılmıyor.
</SYSTEM_MESSAGE>
