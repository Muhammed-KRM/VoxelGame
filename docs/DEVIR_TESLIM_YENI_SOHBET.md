# DEVİR TESLİM — Yeni Sohbet İçin (Son güncelleme: 2026-11-01 18:55)

> Yeni sohbette ilk mesaj olarak şunu ver: **"D:\Oyunum\oyun\docs\DEVIR_TESLIM_YENI_SOHBET.md dosyasını oku ve kaldığın yerden devam et."**

## 0. Proje ve Kurallar
- Proje: **Stratocraft (VoxelGame)**, repo `https://github.com/Muhammed-KRM/VoxelGame.git`, klasör `D:\Oyunum\oyun` (çalışma alanı `d:\Oyunum`), Windows 11.
- Kullanıcıyla **Türkçe** konuş. Kısa, GitHub-markdown. Dosyalara `file:///` linki ver. Kullanıcıya manuel iş verirsen **adım adım ("şuraya gir, şuna tıkla")** anlat.
- Hedef: **0 Error / 0 Warning** (C++20 ctest, Python ruff+pytest, .NET 10 `TreatWarningsAsErrors`, Unity 6 `6000.6.7f1`). Texture VRAM < 16 MB, RAM < 2.5 GB.
- Yol sabit yazma (`D:\oyun` vb. hardcode yok).
- Yetki sırası: **GAME LOGIC BIBLE** (`STRATOCRAFT_ EVRENSEL OYUN TASARIM VE MANTIK ANAYASASI (GAME LOGIC BIBLE) (2).md`) > **MATEMATİKSEL RAPOR** (`STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md`) > `STRATOCRAFT_GELISTIRICI_DOKUMANI.md` > `DURUM_VE_DEVIR.md` > `README.md`.
- Kod kuralları:
  - `Tests/StandaloneCoreTests/Stratocraft.StandaloneCoreTests.csproj` kodu **Unity DLL'siz .NET 10**'da derler → `UnityEngine/Burst/Collections/Jobs/Mathematics` kodunu `#if UNITY_5_3_OR_NEWER` ile koru.
  - `namespace Stratocraft.Core.*` içinde `System.Math.*` yaz (`Stratocraft.Core.Math` gölgelemesi).
  - `.asmdef` döngüsü yasak. Sıra: Core → Rendering/Voxel → Physics → WorldGen → Character; `Inventory` ve `CreaturesAI`, `Character`'a referans verir, yani **Character, Inventory/CreaturesAI'ya referans veremez**. `Stage1InteractivePlayController.cs` `Assets/_Stratocraft/Character/` içinde → ona Inventory/CreaturesAI bağlarken asmdef'leri önce oku.
  - Unity Editor açık (Unity MCP port 7800). `Temp\UnityLockfile` varken ikinci Unity açma. MCP şemaları: `C:\Users\muhammedk\.gemini\antigravity\mcp\unity\*.json`, çağrı `call_mcp_tool` (ServerName `unity`).

## 1. Hangi dökümanlara bakılacak
- Tüm raporlar `D:\Oyunum\oyun\docs\`:
  - Ana anlatım: `FAZ1_VE_FAZ2_KAPSAMLI_MEKANIK_MATEMATIK_VE_COZUM_RAPORU.md`
  - Alt raporlar `docs\faz1_faz2_mekanikler\01..08_*.md` (01 voksel/meshing, 02 worldgen/streaming, 03 domino yıkım, 04 akışkan, 05 karakter/oksijen/termal, 06 madde kristali/envanter, 07 yaratık/NavSpan/AI/VAT, 08 shader/Blender fabrikası).
- Kalan işler için **özellikle 07 ve 08** (Bölüm 4-6: darboğaz çözümleri) okunmalı.

## 2. Bitenler (test edilmiş)
`dotnet test "D:\Oyunum\oyun\Tests\StandaloneCoreTests\Stratocraft.StandaloneCoreTests.csproj"` → **36/36 geçti** (son koşu, NavSpan değişikliklerinden ÖNCE).
- **Modül A** (M01+M02): sabitler, MaterialID, C++20 SIMD WorldGen, halkalar, `.sregion`, SkyOcclusion, floating origin. C++ 6/6 ctest, DLL `Assets\Plugins\x86_64\StratocraftNative.dll`.
- **Modül B**: binary greedy meshing, 62×32 şekil, hybrid surface nets, akışkan kıyı eteği.
- **Modül C** (M03+M04): iki aşamalı Domino çöküş, kemer, Terzaghi, 16-bit akışkan, obsidyen stokiyometrisi.
- **Modül D** (M05): karakter/parkur, oksijen BFS, termal/ıslaklık ODE, 1€ filtre, HUD 3 sn gizleme.
- **Modül E** (M06): `Assets/_Stratocraft/Inventory/*` (264B/16B/128B yapılar, vakum, kılıf, CAS sandık, çanta).
  - Bu oturumda düzeltilen: `MaterialIdCatalog.GetRequiredCrystalTierForTerrain` Bible 2.2.C'ye göre (Toprak=1, Taş=2, Buz/Mermer/Floatstone=3, Bazalt/Magma/NaturalObsidian=4); `MatterCrystalVacuumSystem` Kapı 3 `requiredTier == 0` reddi; `InventoryCasAndMatterCrystalTests` Tier-1/Tier-2 sertlik testi.
  - `Assets/Tests/PlayMode/CharacterAndTacticalBackpackPlayTests.cs`'e Modül D için yeni test eklendi (slide 15 EP, %15 stamina kilidi, çok-chunk oksijen + IronGrating, barotravma, ıslak 3× soğuma, HUD gizleme) — geçti.

## 3. YARIM / DOĞRULANMAMIŞ (ilk iş: derle ve test et!)
Son turda şu dosyalar yazıldı ama **henüz derlenmedi/test edilmedi**:
1. `Assets/_Stratocraft/CreaturesAI/Stratocraft.CreaturesAI.asmdef` → `Stratocraft.Physics` referansı eklendi.
2. `Assets/_Stratocraft/Voxel/Data/VoxelChunkComponent.cs` → `NavVersion` (ushort) eklendi, `SetVoxel` içinde artıyor.
3. `Assets/_Stratocraft/Voxel/Data/NavSpanColumnSummary.cs` → tamamen yeniden yazıldı: `NavSpanHazardFlags` (8 bit), `NavSpanTerrainCode`, `NavSpanRecord32` (Bible/Mat. Rapor 1.2.A 32-bit düzeni), `NavSpanColumn4Layer` (16B, 4 katman, `TryFindContinuousLayer`). Eski `NavSpanColumnSummary` (SurfaceY/Headroom/Hazard) uyumluluk için korundu.
4. `Assets/_Stratocraft/Voxel/Jobs/NavSpanRebuildJob.cs` → yeniden yazıldı: `ulong[4]` bitboard ile `ExecuteMultiLayer`; `Execute` eski API'yi çok-katmandan türetiyor.
   - **Dikkat:** `Execute` içinde `stackalloc` + test `CharacterAndTacticalBackpackPlayTests` satır ~138 `navGrid[2*16+2].SurfaceY == 6` bekliyor. Dirt (2,5,2) → floorY=6; kontrol et. `CA2014`/`CS0675` ve `Math` gölgelemesi (`Stratocraft.Voxel` namespace'inde sorun yok ama kontrol et) uyarılarına bak. `MaterialIdCatalog.WoodPlank/OakWood`, `VoxelBitUtility.GetMaterialId/GetOriginState/IsPhysicallySolid` isimlerini doğrula.
   - Muhtemel sorun: `#nullable enable` ve `readonly struct` + `FieldOffset` birleşimi derleme hatası verebilir; hata çıkarsa `NavSpanRecord32`'den `readonly`/`FieldOffset` kaldırıp düz `uint` alanı yap.
**Komut:** `dotnet test "D:\Oyunum\oyun\Tests\StandaloneCoreTests\Stratocraft.StandaloneCoreTests.csproj" --verbosity minimal` → 36/36 ve 0 warning olmalı; olmazsa düzelt.

## 4. KALAN İŞLER (sırayla)
### Adım A — Modül F1: Yaratıklar + AI (M07; bkz. rapor 07)
Dosyalar (gerçek yollar): `Assets/_Stratocraft/CreaturesAI/CreaturesAndBosses/Creature21BehaviorSystem.cs`, `.../HybridLod/HybridAiLodPromotionSystem.cs`, `.../HybridLod/OffScreenNavSpanStepJob.cs`.
- `Catalog21` şu an **uydurma isimler** (mob_meadow_stag…). Bible **Bölüm 13.0**'a göre değiştir: Lv1(5)+Lv2(5)+Lv3(5)+Lv4(4)+Lv5(2)=21; Goblin İşçi 120-160HP … Hiçlik Avcısı 9500-12000HP (tam liste rapor 07 §1.1 tablosunda). Mevcut testi bozma: `All21Creatures.Count==21`, `GetById(10)` kolu koparınca `"UpperArm.R"` → `HasRightClawOrWeapon` kalkmalı, `TryExecuteSupportPillarStrike` imzası aynı kalmalı (id 10'un `VoxelSiegeBreaker` yeteneği olmalı — Bible'da Kaya/Demir Golemi id 11; testi güncelleyip uygun bir kuşatmacı id'ye çevirebilirsin).
- Yetenek bayrakları ekle: `CAP_HARVEST_HANDS, CAP_MOUNT_GROUND, CAP_MOUNT_FLYING, CAP_SIEGE_AND_SHIP_CREW, CAP_TRENCH_BALLISTIC`; kanat koparsa `WingedFlight` kaybı.
- `RingDifficultyCalculator.cs` (WorldGen) halka eşikleri `0,200,1000,3000,5000,10000,15000` (Modül A'da yapıldıysa doğrula).
- `OffScreenNavSpanStepJob`: `Math.Clamp(...,0,15.99)` ile tek chunk'a kilitleniyor → kaldır; `NavVersion`+Bresenham, `FixedQ20_12` (`Core/Math/FixedPointQ20_12.cs`, tip adı `FixedQ20_12`).
- Hiperbolik Lanchester (`cosh/sinh`, rapor 07 §2.5) ve 64B `OffScreenEntityRecord`, ±8 m histerezis (`EvaluateTierWithHysteresis` test eşiklerini koru: 140→T1, 125→T1, 115→T0).
- Yıkılabilir Voxel A\* (`MaxExpandedNodes=320`, ε=1.25, boss arenasında `OriginState==0` kırılmaz).
- Bunlar için yeni EditMode testleri yaz.

### Adım B — Modül F2: Shader / Python / URP (M08; bkz. rapor 08)
- `Tools/AutonomousAssetFactory/scripts/bake_material_lut.py` (yolu doğrula): yeni MaterialID'ler için palet satırları: 14..19, 52..59, 120..125, 160, 165, 166, 180..195.
- `Assets/_Stratocraft/Rendering/Shaders/StratocraftInput.hlsl`, `StratocraftUberShader.hlsl`, `.shader`: `Color.a` → `ao4=(a>>4)/15`, `wear4=(a&15)/15`; `_FloatingOriginMacroOffset`; `_UseObjectSpaceTriplanar` (düşen yığın/silah/sandık için).
- Yeni `StratocraftFluidSurface.shader` (0 doku; su=12, asit=19 translucent; lav=13 blackbody T⁴).
- `StratocraftVatMobShader.hlsl`: şu an hiçbir `.shader` include etmiyor; `_DismemberBitmask` + (rapor 07 önerisi) 3×4 kemik-matris BAT (≈3 MB VRAM) eklenecek.
- `StratocraftAssetPostprocessor.cs` / `StratocraftUrpBootstrap.cs`: `MaterialPaletteLUT` Uncompressed RGBA32, Point, sRGB kapalı, mip yok. `F3TelemetryOverlayUi.cs`'deki sahte `Mathf.Min(vramMb, 15.9f)` kırpmasını kaldır.

### Adım C — Canlı sahneyi gerçek sistemlere bağla
`Assets/_Stratocraft/Editor/StratocraftStage1SceneBuilder.cs` ve `Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs` (önce asmdef'leri oku!):
- Gerçek 16-bit su/lav + ayrı kıyı-etekli mesh (suya collider yok), baraj kazınca akış → obsidyen/buhar.
- Gerçek `StructuralCollapseSystem` domino (1.5 sn çatlak, rigidbody küme, 1.5 sn sonra `LooseRubble`), 4 renkli hayalet önizleme.
- Madde kristali (OriginState=0) vs kazma/yerleştirme (OriginState=1). **Hata:** PlayController kristal inşasında `ShapeID!=0` iken `OyuncuItemBlogu` yazıyor — Bible'a göre hep `DogalKutle(0)`; kılıf süreleri sabit 1.8 sn değil (bel 0.5/0.6, sırt 0.9/1.3); döndürme tuşu `T` (R değil), `V` yalnızca `C` aktifken basılı tutulur.
- `G` quick-drop kalıcı (`Destroy(...,25f)` kaldır), `I` kumaş çanta, `E` 110° CAS sandık.
- Yaratıklar şu an `Mathf.Sin` ile yerinde sallanıyor → NavSpan/AI'ya bağla.

### Adım D — Kalite kapısı ve Unity doğrulaması
1. `powershell -ExecutionPolicy Bypass -File "D:\Oyunum\oyun\scripts\quality-gate.ps1" -Phase 2`
2. Unity MCP: `script_recompile` → `console_get` (0 Error/0 Warning) → `test_run` EditMode → menü `Stratocraft/Asama 1 - Canli Dunya ve 3B Varlik Sahnesini Kur` → `scene_save`/`editor_save_all` → `capture_scene_view/game_view` ile görsel doğrulama.
3. Kullanıcıya Türkçe, tek anlatımlı özet ver (ana rapor + 8 alt rapor linkleriyle) ve `DURUM_VE_DEVIR.md`'yi güncelle (kullanıcı diğer bilgisayarla senkron çalışıyor).

## 5. Yeni sohbetin ilk 3 hamlesi
1. Bu dosyayı + `docs\faz1_faz2_mekanikler\07_*.md` ve `08_*.md` dosyalarını oku.
2. `dotnet test ...` çalıştır, §3'teki yarım NavSpan değişikliklerini derleme/test hatası kalmayana dek düzelt.
3. Adım A → B → C → D sırasıyla ilerle; her adım sonunda `dotnet test` (0 warning).
