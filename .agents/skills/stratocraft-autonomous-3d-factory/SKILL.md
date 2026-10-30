---
name: stratocraft-autonomous-3d-factory
description: >-
  Stratocraft Zero-Touch 3D Asset & Procedural Texture Factory workflow (Yol A:
  Headless Blender 4.2 + Blockbench + Python trimesh/pygltflib, UV-Free Color32
  StratocraftUberShader, < 16 MB total texture VRAM budget, asset_validator.py,
  and 4-angle 512x512 PNG visual audit). Activate whenever generating, modifying,
  importing, or validating 3D models, rigs, VAT animations, or shaders.
---

# Stratocraft Otonom 3B Varlik ve UV-Free UberShader Fabrikasi Rehberi

## 1. Anayasal Kurallar (Sifir Elle Modelleme & `< 16 MB` VRAM)
- Kullanicidan **asla** elle 3B modelleme, kaplama boyama veya Unity icinde manuel materyal atamasi istenmez.
- Tum varliklar `Tools/AutonomousAssetFactory/Stratocraft_Asset_Manifest.json` uzerinden `scripts/generate-assets.ps1 -Phase <N>` (veya `Tools/bin/blender-4.2/blender.exe --background --python ...` / `Blender MCP`) ile uretilir.
- **UV Kaplama Yasagi (`0` Gomulu Texture):** Modeller `.glb` icine PNG/JPG kaplama gommez. Tum yuzey bilgisi `COLOR_0` (`Color32` / `ubyte4`) vertex kanallarina paketlenir:
  - **`R` (`0..255`):** `MaterialID` (`T_Stratocraft_MaterialLUT_256x16.png` satiri + `32x MicroDetail Array` katmani)
  - **`G` (`0..255`):** Normalize sicaklik (`StratocraftUberShader.hlsl` icinde $T^4$ Stefan-Boltzmann kara cisim isimasina donusur)
  - **`B` (`0..255`):** Keskinlik / Metalik Darbe (`Sharpness` / `WorkHardening`)
  - **`A` (`0..255`):** Tepe Noktasi Ortam Kapanmasi (`Vertex Ambient Occlusion`)

## 2. Soket ve Uzuv Kesim Standartlari
- Karakter ve yaratiklarda uzuv kopmasi icin `Socket_GoreCap_Head`, `Socket_GoreCap_ArmL`, `Socket_GoreCap_ArmR`, `Socket_GoreCap_LegL`, `Socket_GoreCap_LegR` dugumleri bulunur (`StratocraftAssetPostprocessor.cs` icinde baslangicta gizlenir).
- Atolye ve aktif klan yapilarinda manifesto `required_sockets` listesi birebir eksiksiz olmalidir.

## 3. Zorunlu 3 Adimli Oz-Denetim Dongusu
Her yeni veya guncellenen `.glb` varligi icin:
1. `Tools/AutonomousAssetFactory/.venv/Scripts/python.exe Tools/AutonomousAssetFactory/agent_generate_asset.py --phase <N>` calistir (`asset_validator.py`: ucgen butcesi, AABB olcek toleransi, `COLOR_0` varligi, `0` kaplama, soket kontrolu).
2. `BuildLogs/Previews/<asset_id>_contact_sheet.png` (4 acili `512x512` On/Yan/Ust/Izometrik render) dosyasini **`read_file` araciyla bizzat gorerek** geometri ve renk hatalarini denetle.
3. `Assets/Art/Generated/Textures` toplam boyutunun **`< 16.0 MB`** oldugunu dogrula.
