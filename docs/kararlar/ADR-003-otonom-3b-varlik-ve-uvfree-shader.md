# ADR-003: Otonom 3B Varlik Fabrikasi (`Yol A`), UV-Free `COLOR_0` UberShader ve GPU VAT / Uzuv Kopma Mimarisi

- **Durum:** Kabul Edildi (`Faz 2` — `v0.2.0-faz2`)
- **Tarih:** 2026-10-30
- **Ilgili Faz:** Faz 2 — Otonom 3B Modeller, Fiziksel Karakter, Yasayan Canlilar, 3B Kumas Canta, Madde Kristali ve Gorsel Sandiklar

## 1. Baglam ve Problem

Stratocraft projesinde yuzlerce silah parcasinin, 15 parcali 3 katmanli zirh setlerinin, 21 canlinin, 13 Boss'un, 29 atolye/klan cihazinin ve 75 batarya/hologram sablonunun **sifir insan el emegiyle (Zero-Touch)** uretilmesi ve `4 GB VRAM` (`NVIDIA GTX 1650`) sinirinda `< 16.0 MB` toplam kaplama bellegini asmadan cizilmesi gerekmektedir.

## 2. Karar

1. **UV-Free `COLOR_0` (`RGBA Byte`) + Paylasimli LUT/MatCap/3D Noise (`StratocraftUberShader`):**
   - Bireysel `.glb` varliklari hicbir 2B PBR dokusu (Albedo/Normal/Metallic PNG) tasimaz (`0 MB` model basi doku maliyeti).
   - Her kose noktasi `COLOR_0` (`R=MaterialID, G=Wear/Cavity, B=Temp/Blood, A=Flags`) tasir ve GPU'da `T_Stratocraft_MaterialLUT_256x16.exr` + `T_Stratocraft_Noise3D_64.exr` ile prosedurel yuzey olusturulur.
2. **Otonom Uzuv Kopma (`SeveredLimbs/*.glb`) ve 15 Parca Zirh Bosluk Haritasi (`T_ArmorGapMap_64x64_Atlas.png`):**
   - `stratocraft_auto_rig_dismember.py` her canli ve Boss modeli icin 5 anatomik ayrilma kemiginde (`Head, UpperArm.L, UpperArm.R, Thigh.L, Thigh.R`) deliksiz (watertight) kopmus uzuv `.glb` parcalarini ve `Socket_GoreCap_*` dugumlerini otomatik uretir.
   - `stratocraft_armor_autoskin.py` 5 bolge x 3 katman = 15 zirh parcasini radial offset ile giydirir ve `64x64` UV-2 aciklik haritasini (`T_ArmorGapMap_64x64_Atlas.png`) pisirir.
3. **500 Mob Gece Dalgasi VAT (`T_NightWave_500Mob_VAT_Pos.exr` & `_Norm.exr`) ve `4-Byte NavSpan`:**
   - `128m - 512m` araligindaki suruler CPU kemik animasyonu yerine GPU VAT dokularindan (`StratocraftVatMobShader.hlsl`) orneklenir ve `4-Byte NavSpanColumnSummary` uzerinde `10 Hz` adimlanir.

## 3. Sonuclar ve Dogrulama

- `agent_generate_asset.py --phase 2` ile tum `Faz 2` `.glb`, `SeveredLimbs/*.glb`, `.svox`, `T_ArmorGapMap_64x64_Atlas.png` ve VAT `.exr` dosyalari uretilip `asset_validator.py` ve 4 acili `512x512` PNG (`Tools/AutonomousAssetFactory/validation_renders/`) ile dogrulanmistir.
- Toplam Kaplama VRAM kullanimi **`3.382 MB / 16.000 MB`** olarak olculmustur.
