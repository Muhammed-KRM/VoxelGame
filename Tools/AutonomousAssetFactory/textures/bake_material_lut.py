"""Stratocraft Kuresel Materyal LUT (256x16) ve 16x MatCap Atlasi Firinlayici (Faz 0)."""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image


def _define_base_materials() -> dict[int, tuple[tuple[int, int, int], int, int, int, int]]:
    """MaterialID -> (AlbedoRGB, Roughness0_255, Metallic0_255, ProcMode0_3, MicroLayer0_31).

    ProcMode: 0=Mineral/Standart, 1=3B Sam Celigi (Damascus), 2=3B Ahsap Halkasi, 3=Kristal Kirinim.
    """
    return {
        0: ((0, 0, 0), 255, 0, 0, 0),            # Air
        1: ((122, 128, 138), 196, 0, 0, 0),      # Stone
        2: ((126, 90, 60), 225, 0, 0, 1),        # Dirt
        3: ((74, 154, 68), 205, 0, 0, 2),        # Grass
        4: ((214, 194, 138), 205, 0, 0, 3),      # Sand
        5: ((48, 52, 60), 235, 15, 0, 4),        # Bedrock
        6: ((38, 28, 56), 58, 35, 3, 5),         # Natural Obsidian
        7: ((92, 98, 110), 190, 10, 0, 6),       # Basalt
        8: ((136, 134, 130), 210, 0, 0, 7),      # Cobblestone
        9: ((150, 146, 140), 220, 0, 0, 7),      # Gravel
        10: ((168, 218, 248), 54, 20, 3, 12),    # PackedIce
        11: ((228, 72, 22), 140, 15, 0, 14),     # MagmaBlock
        12: ((42, 148, 236), 28, 35, 3, 12),     # FiniteWaterSurface
        13: ((255, 92, 20), 95, 25, 0, 14),      # FiniteLavaSurface
        15: ((108, 102, 96), 230, 0, 0, 7),      # LooseRubble
        # 14 Yeralti Cevheri (30..43) + Komur/Kozmik (44..46)
        30: ((196, 108, 64), 120, 210, 0, 8),    # CopperOre
        31: ((176, 180, 182), 130, 200, 0, 8),   # TinOre
        32: ((148, 152, 160), 115, 225, 0, 9),   # IronOre
        33: ((188, 146, 132), 150, 175, 0, 9),   # BauxiteAluminumOre
        34: ((222, 228, 236), 72, 245, 0, 10),   # SilverOre
        35: ((242, 196, 64), 64, 250, 0, 10),    # GoldOre
        36: ((192, 202, 214), 88, 240, 0, 11),   # TitaniumOre
        37: ((108, 206, 242), 58, 248, 3, 12),   # MithrilOre
        38: ((232, 144, 56), 62, 248, 1, 13),    # OrichalcumOre
        39: ((164, 38, 32), 95, 235, 1, 14),     # HellIronOre
        40: ((248, 238, 184), 48, 252, 3, 15),   # DivineMetalOre
        41: ((148, 104, 246), 44, 220, 3, 16),   # AstralOre
        42: ((64, 96, 82), 52, 252, 1, 17),      # AdamantiteOre
        43: ((38, 22, 58), 40, 245, 3, 18),      # VoidMetalOre
        44: ((36, 36, 40), 220, 10, 0, 6),       # Coal
        45: ((22, 12, 36), 30, 255, 3, 19),      # DarkMatter
        46: ((255, 224, 140), 20, 255, 3, 20),   # StarCore
        # Alasimlar & Ahsaplar
        50: ((188, 132, 68), 96, 235, 0, 21),    # Bronze
        51: ((168, 176, 188), 82, 245, 0, 22),   # Steel
        60: ((152, 162, 178), 64, 250, 1, 23),   # DamascusSteel (ProcMode = 1)
        70: ((156, 110, 68), 188, 0, 2, 24),     # OakWood (ProcMode = 2)
        71: ((98, 70, 50), 168, 15, 2, 25),      # Ironwood (ProcMode = 2)
        72: ((112, 164, 158), 152, 25, 2, 26),   # SpiritWood (ProcMode = 2)
        73: ((184, 216, 132), 140, 35, 2, 27),   # YggdrasilBranch
        74: ((146, 114, 78), 218, 0, 0, 24),     # TacticalCloth
        75: ((112, 74, 48), 200, 0, 0, 24),      # LeatherPadding
        76: ((232, 224, 204), 135, 10, 0, 28),   # BoneIvory
        77: ((86, 76, 72), 212, 0, 0, 29),       # CreatureFurAsh
        78: ((252, 168, 44), 76, 160, 3, 30),    # PhoenixFeatherGold
    }


def bake_material_lut_256x16(out_png_path: Path) -> Path:
    """256x16 RGBA32 Materyal LUT dokusunu uretir (~16 KB VRAM).

    Satir 0: RGB = Taban Albedo, A = Roughness
    Satir 1: R = Metallic, G = ProcMode (*85), B = MicroLayerIdx (*8), A = MatCapIdx (*17)
    Satir 2..15: Termal Blackbody kizarma (T^4) ve derinlik / asinma gecis gradyani.
    """
    out_png_path.parent.mkdir(parents=True, exist_ok=True)
    lut = np.zeros((16, 256, 4), dtype=np.uint8)
    defined = _define_base_materials()

    for mat_id in range(256):
        if mat_id in defined:
            albedo, rough, metal, proc_mode, micro_idx = defined[mat_id]
        elif mat_id >= 240:
            # Zindan Kutsal Koruma Bloklari (DungeonSanctumImmune)
            albedo = (82, 88, 112)
            rough, metal, proc_mode, micro_idx = 140, 90, 3, 28
        else:
            albedo = (
                int((mat_id * 37 + 90) % 170 + 55),
                int((mat_id * 73 + 70) % 170 + 55),
                int((mat_id * 113 + 50) % 170 + 55),
            )
            rough = int(110 + (mat_id * 19) % 120)
            metal = 220 if (30 <= mat_id <= 69) else 0
            proc_mode = 0
            micro_idx = mat_id % 32

        matcap_idx = 0 if metal < 128 else (1 + (mat_id % 15))
        lut[0, mat_id] = [albedo[0], albedo[1], albedo[2], rough]
        lut[1, mat_id] = [metal, min(255, proc_mode * 85), min(255, micro_idx * 8), min(255, matcap_idx * 17)]

        # Satir 2..15: Demirci orsunde T^4 Blackbody kizarma + yuzey varyasyonu
        for row in range(2, 16):
            heat_t = (row - 2) / 13.0
            glow_r = int(np.clip(albedo[0] * (1.0 - 0.5 * heat_t) + 255.0 * (heat_t**1.3), 0, 255))
            glow_g = int(np.clip(albedo[1] * (1.0 - 0.6 * heat_t) + 190.0 * (heat_t**2.2), 0, 255))
            glow_b = int(np.clip(albedo[2] * (1.0 - 0.8 * heat_t) + 110.0 * (heat_t**3.5), 0, 255))
            lut[row, mat_id] = [glow_r, glow_g, glow_b, rough]

    Image.fromarray(lut, mode="RGBA").save(out_png_path, format="PNG", optimize=True)
    return out_png_path


def bake_matcap_atlas_16x(out_png_path: Path, tile_res: int = 128) -> Path:
    """4x4 = 16 adet sifir-isik maliyetli MatCap kuresi iceren 512x512 RGB atlasi uretir (< 1.1 MB VRAM)."""
    out_png_path.parent.mkdir(parents=True, exist_ok=True)
    atlas_res = tile_res * 4
    atlas = np.zeros((atlas_res, atlas_res, 3), dtype=np.uint8)

    lin = np.linspace(-1.0, 1.0, tile_res, dtype=np.float32)
    gx, gy = np.meshgrid(lin, -lin)
    r2 = gx * gx + gy * gy
    inside = r2 <= 1.0
    gz = np.sqrt(np.maximum(0.0, 1.0 - r2))

    key_dir = np.array([-0.38, 0.58, 0.72], dtype=np.float32)
    key_dir /= np.linalg.norm(key_dir)
    rim_factor = np.clip(1.0 - gz, 0.0, 1.0) ** 2.4

    # 16 farkli malzeme tepki profili (tas, deri, demir, celik, altin, mithril, kristal, obsidyen...)
    tints = [
        ((185, 188, 194), 8.0, 0.15),
        ((210, 218, 230), 28.0, 0.65),
        ((245, 205, 95), 42.0, 0.85),
        ((140, 220, 255), 56.0, 0.90),
        ((170, 130, 95), 6.0, 0.10),
        ((205, 135, 85), 24.0, 0.55),
        ((165, 120, 245), 64.0, 0.95),
        ((95, 105, 120), 18.0, 0.40),
        ((235, 240, 248), 72.0, 0.95),
        ((195, 70, 60), 32.0, 0.70),
        ((110, 165, 135), 22.0, 0.50),
        ((250, 240, 190), 80.0, 1.00),
        ((80, 65, 105), 48.0, 0.80),
        ((150, 145, 135), 10.0, 0.20),
        ((220, 165, 90), 36.0, 0.75),
        ((125, 195, 215), 50.0, 0.85),
    ]

    ndotl = np.maximum(0.0, gx * key_dir[0] + gy * key_dir[1] + gz * key_dir[2])
    half_vec = key_dir + np.array([0.0, 0.0, 1.0], dtype=np.float32)
    half_vec /= np.linalg.norm(half_vec)
    ndoth = np.maximum(0.0, gx * half_vec[0] + gy * half_vec[1] + gz * half_vec[2])

    for idx, (rgb_base, shininess, spec_weight) in enumerate(tints):
        ty = (idx // 4) * tile_res
        tx = (idx % 4) * tile_res
        spec = (ndoth**shininess) * spec_weight * 255.0
        diffuse = 0.25 + 0.75 * ndotl
        tile = np.zeros((tile_res, tile_res, 3), dtype=np.uint8)
        for ch in range(3):
            val = rgb_base[ch] * diffuse + spec + rim_factor * 42.0
            tile[:, :, ch] = np.where(inside, np.clip(val, 0, 255), rgb_base[ch] * 0.22).astype(np.uint8)
        atlas[ty : ty + tile_res, tx : tx + tile_res] = tile

    Image.fromarray(atlas, mode="RGB").save(out_png_path, format="PNG", optimize=True)
    return out_png_path
