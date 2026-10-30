# Alt Ajan Araştırma Raporu: Headless Blender 4.2 LTS bpy Prosedürel Üretim, Color32 Repack ve glTFast

- **Tarih:** 2026-10-30
- **Alt Ajan ID:** af238289-2a83-4c9b-959c-3a1c8c2e88e7
- **Ana Rapor:** [../FAZ1_FAZ2_SORUN_VE_COZUM_MIMARI_RAPORU.md](../FAZ1_FAZ2_SORUN_VE_COZUM_MIMARI_RAPORU.md)

---

# Stratocraft Otonom 3B Fabrika: Yerel Denetim ve Araştırma Raporu

## A. Yerel bulgular (dosyaları değiştirmedim)

**1. Blender hiç çağrılmıyor. Tüm meshler saf Python + pygltflib ile primitiflerden kuruluyor.**
- `generators/base_generator.py` L107-269 `_append_box_part`: her "parça" bir AABB'nin içine sığdırılmış 8 kenarlı, 3 halkalı prizma. Kapaklarla birlikte parça başına **48 üçgen** çıkıyor. Profil seçenekleri crystal, horn, wing, wheel ve sculpted; aralarındaki tek fark halka ölçekleri.
- L272-395 `write_color32_glb`: sabit 4 bufferView (pos, nrm, col u8, idx u16) yazıyor. Socket'ler kök node'un çocuğu olan boş node'lar.
- `agent_generate_asset.py` L106 ve L112: `render_4angle_contact_sheet(..., prefer_blender_cli=False)` sabit. L172: `force_cpu_fallback=True` sabit.
- `neural_sculpt_pipeline.py` sahte: her durumda `generate_creature_or_boss` çağırıyor. GPU kontrolü de yalnızca `which("instant-meshes")`.
- `procedural_creature_builder.py`: kurt ve phoenix 9-11 prizmadan oluşuyor. `use_neural_path_b` yok sayılıyor (L21).
- `requirements.txt`: numpy, scipy, Pillow, trimesh, pygltflib, midvoxio. **bpy yok.** Validator'da trimesh kullanılmıyor, yani manifold kontrolü yapılmıyor.

**2. Model detayı: 32 GLB'nin hepsi gerçekten primitif.**
- Üçgen sayıları: silah 144, kalibrasyon örsü 144, 16 zırh parçasının her biri 288, kurt 432, phoenix 480, trebuchet 432, kristal 336, örs istasyonu 288, kopmuş uzuvlar 96.
- Hepsinde `skins=0` ve `animations=0`. Yani "rig/dismember" diye bir şey yok; kopmuş uzuvlar da ayrı prizma GLB'leri.
- Zırh dosyalarının hash'leri farklı ama hepsi aynı 288 üçgende, yani şablon aynı.
- Bütçeye göre çok düşük poligonlular ama faset yoğunluğu ve siluet "Stylized Sculpted Fantasy" seviyesinin çok altında. `Reports/AssetContactSheets/mob_ash_stalker_vat500_4View.png` dosyasına baktım: kahverengi sütun yığını, okunabilir bir kurt silueti yok.

**3. Kontak sayfaları gerçek render değil.**
- `contact_sheet_renderer.py` L16-85: PIL ile painter's algorithm, Lambert gölgelendirme ve polygon outline. Perspektif, AO ve gerçek shader yok.
- Blender yolu (L115-134) `shutil.which("blender")` üzerinden arıyor ve PATH'te Blender yok. Üstelik tek açılı ve ışıksız.
- 9 PNG var: `Reports/AssetContactSheets/*_4View.png` ve `BuildLogs/Previews/*`.

**4. Diğer sahte veya eksik kısımlar**
- `rigging_and_vat/vat_texture_baker.py` L23-34: geçerli bir OpenEXR yazmıyor. Yalnızca magic+version+ham fp16 var, header yok. İçerik de mesh'ten bağımsız sinüs dalgası (L56-58).
- `asset_validator.py` L58-190 şunları kontrol ediyor: AABB ±%2, pivot, üçgen bütçesi, socket isimleri, MatID≠0, PNG'nin var olup olmadığı. **Kontrol etmedikleri:** manifold/watertight, +Z ileri yönü, normal yönleri, socket konumunun mesh'e göre mantıklı olup olmadığı. Sadece PNG'nin varlığına bakıyor, içeriğine değil.

**5. Ortam (önemli)**
- Blender 4.2.16 LTS **kurulu ve şu an açık**, MCP eklentisi bağlı (addon 1.8, protocol 15). `get_addon_status` ile doğruladım.
- Exe yolu `D:\oyun\Tools\bin\blender-4.2\blender.exe`. Bu klasör `.gitignore`'da (`Tools/bin/`), PATH'te değil. Yol `$PSScriptRoot` veya repo köküne göre göreceli çözülmeli.
- `Tools\bin\gltfpack.exe` da mevcut. Ayrıca `material_maker` ve `llvm-mingw` var.
- GPU: GTX 1650 (4 GB). Unity sürümü 6000.6.7f1, URP 17.6.0.
- Blender MCP generator'ları (tripo, hunyuan, …) ve kütüphaneler kapalı.

**6. Blender 4.2 glTF exporter hakkında doğrulanmış kritik bilgi** (canlı Blender'da `io_scene_gltf2` v4.2.83 kaynağını okudum)
- Mevcut parametreler: `export_vertex_color` ∈ {MATERIAL, ACTIVE, NONE}, `export_all_vertex_colors`, `export_active_vertex_color_when_no_material`, `export_attributes`, `export_extras`, `export_yup`, `export_apply`.
- `gltf2_blender_gather_primitive_attributes.py`: **Byte Color attribute'lar COLOR_0'a UNSIGNED_SHORT normalized (16 bit) olarak yazılıyor.** Değerler `.color` ile lineer okunuyor. Byte Color sRGB saklandığı için **R=MaterialID gibi tam sayı paketleme bozulur.**
- FLOAT_COLOR attribute'lar FLOAT olarak yazılıyor.
- Sonuç: Blender çıktısı mevcut "COLOR_0 = UNSIGNED_BYTE" sözleşmesine doğrudan uymaz. Çözüm:
  - MatID/Temp/Sharp/AO değerlerini **FLOAT_COLOR (POINT ya da CORNER)** veya `_MATID` gibi alt çizgili özel attribute'lara yaz (`export_attributes=True`).
  - Ardından pygltflib ile **post-pass** yapıp COLOR_0'ı `round(x*255)` ile UNSIGNED_BYTE'a yeniden paketle. Mevcut `write_color32_glb` ve `_read_accessor_array` bunun için yeniden kullanılabilir.

**7. Unity tarafı**
- `StratocraftStage1SceneBuilder.cs` L1035-1155 `LoadOrConvertGlbToUnityMesh` **çok kırılgan**:
  - (a) JSON regex ile okunuyor (L1071-1073) ve **ilk 4 bufferView'ın sırasıyla pos/nrm/col/idx olduğu** varsayılıyor.
  - (b) Accessor componentType, byteOffset, byteStride, normalized okunmuyor. COLOR'ın 4 bayt, index'in uint16 olduğu varsayılıyor.
  - (c) Tek mesh ve tek primitive varsayılıyor. Node TRS ve socket node'ları tamamen yok sayılıyor.
  - (d) Skin, morph ve çoklu malzeme desteklenmiyor.
  - (e) `vertCount>65000` kontrolü yapılıyor ama index'ler hep `ToUInt16` ile okunuyor.
  - Blender'ın ürettiği GLB'de bufferView sırası farklı olur, COLOR_0 u16 gelir, socket'ler kaybolur. Yani **bozulur.**
- `StratocraftAssetPostprocessor.cs` L20-35 ve L61-103: `OnPreprocessModel/OnPostprocessModel` yalnızca ModelImporter'da (FBX/OBJ/…) tetiklenir. **.glb için Unity'nin yerleşik importer'ı yok**, bu yüzden bu kod GLB'lerde fiilen ölü. glTFast kendi ScriptedImporter'ını kullanır.
- `Packages/manifest.json`: **`com.unity.cloud.gltfast` yok.**

## B. Web araştırması (özet)

**1. Headless bpy ile prosedürel low-poly**
- Komut: `blender.exe --background --factory-startup --python gen.py -- --spec x.json`. Argümanlar `sys.argv[sys.argv.index("--")+1:]` ile alınır. Subprocess ile çağrılıp returncode ve stdout JSON'u okunur.
- Teknikler:
  - bmesh ve `bpy.data.meshes.from_data` ile taban mesh.
  - Skin modifier ile yaratık gövdesi (vertex+edge iskeletinden).
  - Mirror + Solidify + Bevel (segments=1 ile chamfer görünümü).
  - Boolean (EXACT) ile CSG.
  - Geometry Nodes ağacı Python'dan `bpy.data.node_groups.new(...,'GeometryNodeTree')` ile kurulur.
  - Fasetli görünüm için `Decimate(decimate_type='DISSOLVE', angle_limit≈5-15°)` (planar) veya COLLAPSE, ardından `polygon.use_smooth=False`, yani flat shading.
- Socket'ler: Empty nesneleri glTF'e node olarak çıkar. `export_extras=True` ile custom property'ler de taşınır.
- Export ayarları: `export_format='GLB', export_yup=True, export_apply=True, export_vertex_color='ACTIVE'` (veya NONE + `export_attributes=True`), `export_normals=True`, `export_texcoords=False`.
- Blender'da ileri yön −Y'dir. Unity +Z ileri için modelleri Blender'da **−Y'ye bakacak** şekilde kur (glTF +Z ileri, Unity importer'ı da +Z'ye çevirir). Validator'a yön testi ekle: socket veya "burun" konumu +Z tarafında olmalı.
- Headless'ta `bpy.ops` için `bpy.context.temp_override(...)` gerekir. Mümkün olduğunca modifier ve bmesh API'si tercih edilmeli.
- Hazır üretici kaynakları: Laurum Lucky "Procedural Swords" GN (CC0, YouTube + Gumroad). Pamir Bal Sword Generator (ücretli). Sverchok GPL-3 (yalnızca pipeline aracı olarak sorun yok).

**2. Unity'de glTF**
- Resmi paket `com.unity.cloud.gltfast` 6.x. Editor import'u ScriptedImporter (`GltfImporter`) ile yapar, sub-asset olarak Mesh/Material/Prefab üretir.
- COLOR_0'ı Unity vertex color stream'ine taşır. u8/u16/float desteği spec'e uygun.
- Node'lar Transform olarak korunur, yani Socket_* boş GameObject olarak gelir.
- Materyali kendi shader'ımıza zorlamanın yolları:
  - (a) `IMaterialGenerator` (ya da ShaderGraph generator'ından türetme). Runtime'da `GameObjectInstantiator`'a verilir.
  - (b) Editor import için en basit ve sağlam yol: import sonrası `AssetPostprocessor.OnPostprocessAllAssets` içinde prefab renderer'larına `M_Stratocraft_UberMaterial` atamak, ya da glTFast prefab'ından prefab variant üretmek.
- **Öneri:** glTFast'ı ekle (Package Manager'daki güncel 6.x sürümü, `package_search` ile doğrulanmalı). Böylece özel regex parser kaldırılır. glTFast istenmezse parser en azından accessor tabanlı yeniden yazılmalı: JsonUtility/Newtonsoft ile DTO, componentType/normalized/stride/byteOffset, çoklu primitive, node hiyerarşisi.
- Not: glTF vertex color lineer kabul edilir. Unity'de color space dönüşümüne dikkat. MatID'yi UV2 veya `colors32` olarak ham okumak en güvenlisi; bugünkü kod bunu zaten `uv2` ile yapıyor.

**3. Auto-rig / VAT**
- **UniRig** (VAST-AI-Research): MIT lisanslı, ancak CUDA ve **≥8 GB VRAM** istiyor, Linux ağırlıklı. GTX 1650'de **pratik değil.**
- Yapılabilir alternatif: **Rigify** (Blender'la gelir, GPL ama pipeline aracı). `rigify.generate` Python API'si veya kendi armature'ımızı bmesh/edit_bones ile kurup `parent_set(type='ARMATURE_AUTO')` (temp_override ile). Prosedürel yaratıkta iskelet zaten bilindiği için bone'ları üretim parametrelerinden doğrudan yaratmak en deterministik yol. Skin modifier'ın "Create Armature" seçeneği de işe yarar.
- Dismember: kemik başına ayrı mesh parçası ve kesik noktalarında GoreCap socket'leri.
- VAT seçenekleri:
  - **fuqunaga/VatBaker**: MIT, Unity tarafında AnimationClip + SkinnedMeshRenderer'dan bake eder, OpenUPM `ga.fuquna.vatbaker`. En uygun seçenek.
  - **OpenVAT** (sharpen3d): GPL-3 Blender eklentisi. Bake aracı olarak kullanılabilir, ama Unity runtime shader'larının GPL olabileceğine dikkat.
  - Mevcut `vat_texture_baker.py` silinmeli veya gerçek bake ile değiştirilmeli.

**4. Görsel öz-denetim**
- Headless Blender'da orto kameralarla Workbench (en hızlı, GPU gerektirmez: `shading.light='STUDIO'`, `color_type='ATTRIBUTE'` ile vertex color görünür, `show_cavity`/outline açık) veya EEVEE (`BLENDER_EEVEE_NEXT`) ile 4 render (ön/yan/üst/3-4 izometrik, 256² veya 512²) alınır, PIL ile 2x2 grid'e birleştirilir.
- Blender açıkken MCP aracı `look(mode="angles", views=[front,right,top,three_quarter], shading="solid"|"rendered")` aynı işi tek çağrıda yapıyor ve görseli doğrudan ajana getiriyor.

**5. AI 3D üretimi (GTX 1650 4 GB)**
- TRELLIS (MIT): 16 GB+, TRELLIS.2 24 GB+.
- TripoSG (MIT): ≥8 GB.
- Hunyuan3D-2-mini: ~5-6 GB, yalnızca geometri modunda. Lisansı Tencent Community; **AB/UK/Güney Kore bölgesini hariç tutuyor**, ticari kullanımda risk.
- 4 GB'ta yerel kullanım **uygulanabilir değil.** Bulut API (Tripo/Meshy/Hyper3D, MCP üzerinden) kredi ister ve deterministik değildir. Üstelik çıkan yüksek poligonlu, dokulu mesh "UV-free Color32" sözleşmesine uymaz (retopo + vertex-color bake gerekir).
- **Öneri:** Yol B'yi "opsiyonel / ileri PC" olarak bırak. Ana yol: deterministik bpy prosedürel üretim (Yol A). gltfpack (zaten var) yalnızca quantize ve LOD için (`-si 0.5` simplify) kullanılmalı.

**6. Stil referansı**
- DRG: orta poligon + **hard edge / flat shading**. Detay normal map ile değil geometriyle (bevel, oluk) verilir. Düz renk ve gradient, vertex color ağırlıklı.
- V Rising: güçlü silüet, mid-poly, el boyaması.
- Enshrouded: voxel arazi + klasik high→low organik varlıklar.
- Bizim için hedef: **güçlü silüet + 300-3k üçgen + chamfer bevel + planar decimate fasetleri + MatID başına düz renk + AO/cavity'nin vertex alpha'ya bake'i.** Bugünkü 144-480 üçgenli prizmalar silüet testinden geçemiyor.

## C. Öneri: neyi tutmalı, neyi yeniden yazmalı

**Tut:**
- `Stratocraft_Asset_Manifest.json` sözleşmesi (`ManifestEntry`).
- `asset_validator.py` iskeleti (AABB, pivot, bütçe, socket).
- `write_color32_glb` ve `_read_accessor_array`, Color32 post-pass'i için.
- voxel_template (.svox) ve doku bake'leri (ayrı denetlenmeli).
- PIL tabanlı renderer yalnızca "Blender yok" CI fallback'i olarak kalabilir.

**Yeniden yaz:**
1. `generators/blender/` altında bpy script'leri. Kategori başına 1 dosya:
   - `bpy_weapon.py`: kesit profili + extrude, bevel, mirror.
   - `bpy_armor.py`: gövde proxy'sinden shrinkwrap + solidify + planar decimate.
   - `bpy_creature.py`: Skin modifier iskeleti, subdiv 1, planar decimate, armature.
   - `bpy_structure.py`
   
   Ortak `bpy_common.py` şunları içermeli: sahne reset, FLOAT_COLOR "SC_Color32" yazımı (R=MatID/255, G, B, A=AO; AO için Blender'da `Cavity` veya pointiness ya da ray AO hesabı), Socket_* empty'leri, BottomCenter pivot, −Y ileri, glTF export ve `look`'a eşdeğer 4 açılı Workbench render.
2. `blender_runner.py`: Blender exe'yi env `STRATO_BLENDER`, ardından `<repo>/Tools/bin/blender-4.2/blender.exe`, ardından `which` sırasıyla göreceli çözer. `--background --factory-startup --python ... -- --spec` ile çalıştırır, timeout ve JSON rapor okur. `agent_generate_asset.generate_single_asset` buraya yönlendirilir.
3. Post-pass `color32_repack.py`: COLOR_0 (float/u16) → UNSIGNED_BYTE ve MatID doğrulaması. Ardından gltfpack ile LOD1/LOD2 (`-si`).
4. Validator'a eklenecekler: trimesh `is_watertight`/`is_winding_consistent` (veya parça bazlı kontrol), forward-axis heuristiği, "PNG gerçek render mi" kontrolü (boş/tek renk piksel oranı), üçgen **alt sınırı** (ör. bütçenin ≥%15'i; primitif kaçağını engeller).
5. `neural_sculpt_pipeline.py` ve `vat_texture_baker.py`'deki sahte uygulamalar ya kaldırılmalı ya açıkça "NotImplemented" verip raporlanmalı.
6. Unity:
   - `com.unity.cloud.gltfast` eklenmeli.
   - `LoadOrConvertGlbToUnityMesh` kaldırılmalı veya glTFast'a delege edilmeli.
   - Postprocessor `OnPostprocessAllAssets` ile glTFast prefab'ına uber material atamalı ve GoreCap'leri gizlemeli.
   - MatID için `colors32`'nin ham değer olduğunu doğrulayan bir editor testi eklenmeli.

**Ajan iş akışı (MCP ve CLI)**
- **Blender GUI açık (MCP bağlı):** Tasarım ve iterasyon için `execute_blender_code` ile aynı `bpy_*.py` modülü import edilip çağrılır (`sys.path`'e `Tools/AutonomousAssetFactory/generators/blender` eklenir). Sonra `get_scene_info` ile bbox ve üçgen sayısı, `look(mode="angles", shading="solid")` ile görsel kontrol yapılır. Kullanıcının sahnesini bozmamak için yeni bir scene veya collection'da çalışılır ve temizlenir. Shader node'lar type ile aranır, enum'lar okunarak kullanılır (MCP instructions.md'de geçen kurallar).
- **Headless (CI / quality-gate / ikinci PC):** Aynı script `blender_runner.py` ile `--background` çalışır, GLB + 4 açılı PNG + JSON yazar. Ardından validator ve post-pass gelir. Kalıcı çıktıyı tek kaynak olarak bu yol üretir, MCP yalnızca ön izleme içindir. Bu sayede iki PC'de deterministik sonuç alınır; Blender `Tools/bin` altında ama gitignore'da olduğu için `sync-pc` ile ayrıca taşınması gerekir.

## D. Kaynaklar
- Blender glTF exporter: https://docs.blender.org/manual/en/4.2/addons/import_export/scene_gltf2.html (vertex color ve attributes bölümleri). Yerelde doğrulanan kaynak: `io_scene_gltf2/blender/exp/gltf2_blender_gather_primitive_attributes.py`.
- Blender CLI: https://docs.blender.org/manual/en/4.2/advanced/command_line/arguments.html
- Decimate: https://docs.blender.org/manual/en/4.2/modeling/modifiers/generate/decimate.html
- Skin modifier: https://docs.blender.org/manual/en/4.2/modeling/modifiers/generate/skin.html
- glTFast: https://docs.unity3d.com/Packages/com.unity.cloud.gltfast@6.0/manual/index.html (ImportEditor, Materials / IMaterialGenerator). GitHub: https://github.com/Unity-Technologies/com.unity.cloud.gltfast
- UniRig (MIT, ≥8 GB CUDA): https://github.com/VAST-AI-Research/UniRig
- VatBaker (MIT): https://github.com/fuqunaga/VatBaker, https://openupm.com/packages/ga.fuquna.vatbaker/
- OpenVAT (GPL-3): https://github.com/sharpen3d/openvat
- TRELLIS (MIT): https://github.com/microsoft/TRELLIS
- TripoSG (MIT, 8 GB): https://github.com/VAST-AI-Research/TripoSG
- Hunyuan3D-2 (Tencent Community License, bölge kısıtı): https://github.com/Tencent/Hunyuan3D-2
- Blender MCP (MIT): https://github.com/ahujasid/blender-mcp
- Procedural Swords GN (CC0): laurumlucky.gumroad.com/l/swords
- gltfpack: https://meshoptimizer.org/gltf/

Not: Web arama özetlerindeki URL'lerin bir kısmı yönlendirme bağlantısıydı; yukarıdakiler doğrudan kanonik adresler. glTFast'ın tam sürüm numarasını Unity `package_search` ile doğrulamanızı öneririm.
</SYSTEM_MESSAGE>
