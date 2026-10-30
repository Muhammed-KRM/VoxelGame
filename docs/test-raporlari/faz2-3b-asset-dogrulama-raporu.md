# Faz 2 Otonom 3B Varlik, Uzuv Kopma, VAT ve `.svox` Dogrulama Raporu (`v0.2.0-faz2`)

- **Tarih:** 2026-10-30
- **Komut:** `python Tools/AutonomousAssetFactory/agent_generate_asset.py --phase 2`
- **Genel Sonuc:** **PASS (`0` Hata, `8/8` Ana Manifest Varligi + `15/15` Zirh Parcasi + `10/10` SeveredLimb `.glb` + `4/4` `.svox` + VAT `.exr`)**
- **Toplam Kaplama VRAM Kullanimi:** **`3.382 MB / 16.000 MB`** (`%21.1` butce kullanimi)

## 1. Uretilen ve `asset_validator.py` + 4-Acili PNG Ile Dogrulanan `.glb` Varliklari

| Asset ID | Kategori | Cikti Yolu | Ucgen / Butce | Zorunlu Soketler | `COLOR_0` | 4-Acili PNG | Durum |
| :--- | :--- | :--- | :---: | :--- | :---: | :---: | :---: |
| `faz0_calibration_anvil_pedestal` | Calibration | `Assets/Art/Generated/Calibration/faz0_calibration_anvil_pedestal.glb` | `88 / 800` | `Socket_WorkSurface, Socket_Origin` | `RGBA Byte` | 4x PNG | **PASS** |
| `wpn_hilt_iron_bastard` | Weapons_Modular | `Assets/Art/Generated/Weapons_Modular/wpn_hilt_iron_bastard.glb` | `96 / 600` | `Socket_Grip_Main, Socket_Grip_Offhand, Socket_Pommel, Socket_Blade_Mount, Socket_Crossguard_L, Socket_Crossguard_R` | `RGBA Byte` | 4x PNG | **PASS** |
| `wpn_spear_ash_shaft` | Weapons_Modular | `Assets/Art/Generated/Weapons_Modular/wpn_spear_ash_shaft.glb` | `64 / 500` | `Socket_Grip_Main, Socket_Grip_Offhand, Socket_Spearhead_Mount, Socket_ButtSpike` | `RGBA Byte` | 4x PNG | **PASS** |
| `armor_set_iron_vanguard` (+15 alt parca) | Armors_15Piece | `Assets/Art/Generated/Armors_15Piece/armor_set_iron_vanguard.glb` | `120 / 2400` | `Socket_Head, Socket_Torso, Socket_Back_Holster_L, Socket_Back_Holster_R, Socket_Waist_Holster_L, Socket_Waist_Holster_R` | `RGBA Byte` | 4x PNG | **PASS** |
| `wksp_anvil_16x32x4` | Workshops_29Devices | `Assets/Art/Generated/Workshops_29Devices/wksp_anvil_16x32x4.glb` | `92 / 1200` | `Socket_AnvilGrid_Origin, Socket_Hammer_Impact, Socket_Harmonizer_Mount` | `RGBA Byte` | 4x PNG | **PASS** |
| `struct_clan_crystal_totem` | Structures_24Active | `Assets/Art/Generated/Structures_24Active/struct_clan_crystal_totem.glb` | `84 / 1500` | `Socket_Crystal_Core, Socket_Aura_Emitter, Socket_Ground_Anchor` | `RGBA Byte` | 4x PNG | **PASS** |
| `mob_dire_boar_t1` (+5 `SeveredLimbs`) | Creatures_21 | `Assets/Art/Generated/Creatures_21/mob_dire_boar_t1.glb` | `88 / 1800` | `Socket_Jaw_Hitbox, Socket_GoreCap_Head, Socket_GoreCap_Leg_FL, Socket_GoreCap_Leg_FR` | `RGBA Byte` | 4x PNG | **PASS** |
| `boss_phoenix_ember_avatar` (+5 `SeveredLimbs`) | Bosses_13_And_Disasters_12 | `Assets/Art/Generated/Bosses_13_And_Disasters_12/boss_phoenix_ember_avatar.glb` | `72 / 3500` | `Socket_Wing_L, Socket_Wing_R, Socket_EmberEgg_Spawn, Socket_Chest_Weakness` | `RGBA Byte` | 4x PNG | **PASS** |

## 2. Uretilen Ozel Dokular ve `.svox` Sablonlari

- **Zirh Aciklik Haritasi:** `Assets/Art/Generated/Armors_15Piece/T_ArmorGapMap_64x64_Atlas.png` (`64x64`, koltuk alti/eklem acikliklari `%100` acik, plaka merkezleri `%0` kapali).
- **500 Mob Gece Dalgasi VAT Dokulari:** `Assets/Art/Generated/Textures/T_NightWave_500Mob_VAT_Pos.exr` ve `T_NightWave_500Mob_VAT_Norm.exr` (`128x64 RGBA16F`).
- **`.svox` Voxel/Batarya/Zindan Sablonlari (`Assets/StreamingAssets/VoxelTemplates/`):**
  - `bat_t1_lightning_rod.svox`, `bat_t2_obsidian_wall.svox`, `bat_t3_aegis_dome.svox`, `dng_ring1_sanctum_gate.svox` (Magic `SVOX`, `32-bit VoxelCell` + `BlokKokenDurumu` dogrulandi).
