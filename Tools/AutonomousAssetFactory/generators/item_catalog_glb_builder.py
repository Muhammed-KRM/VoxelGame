"""Stratocraft Otonom 3B Fabrika — PT-07 Yuksek Detayli 3B Esya ve Alet Katalogu Ureticisi (Blender 4.2).

Kullanim (Headless Blender 4.2 ile, 0 gomulu doku, %100 UV-Free COLOR_0 Color32):
    blender --background --python Tools/AutonomousAssetFactory/generators/item_catalog_glb_builder.py

Uretilen 24 Yuksek Detayli 3B Model (Assets/Art/Generated/Items/*.glb + Weapons_Modular/tool_axe_iron.glb):
    1. item_tool_axe_iron.glb        (+ tool_axe_iron.glb) — Sakalli Celik Oduncu/Savas Baltasi
    2. item_tool_primitive_axe.glb   — Lif Sargili Yontulmus Cakmaktasi Ilkel Balta
    3. item_tool_stone_hammer.glb    — Bogumlu Sapli & Lif Sargili Agir Tas/Demir Cekic
    4. item_tool_primitive_knife.glb — Kemik/Ahsap Kabzali Yontulmus Cakmaktasi Av Bicagi
    5. item_tool_micro_chisel.glb    — Pirinc Yuksklu & Jilet Pahli 4x4x4 Mimari Mikro-Keski
    6. item_tool_shovel_spade.glb    — T-Kabzali & Perçinli Celik Kurek
    7. item_torch_campfire.glb       — Recineli Bez Sargili & T^4 Kor Alevli Mesale / Kamp Atesi
    8. item_fluid_flask.glb          — Deri Askili, Pirinc Boyunlu & Mantar Tipali Sivi Matarasi
    9. item_fluid_bucket.glb         — Cift Demir Cemberli & Kavisli Kulplu Ahsap Fici Kova
   10. item_fluid_cauldron.glb       — 3 Ayakli, Kulplu & Disa Kivrik Agizli Dokum Kazan
   11. item_flint_stone.glb          — Konkoidal Kirikli & Keskin Sirtli Cakmaktasi Cekirdegi
   12. item_rough_stone.glb          — Cok Yuzeyli Dogal Nehir/Kaya Tasi
   13. item_ore_chunk.glb            — Kristal/Metalik Damarli Zengin Cevher Kayasi
   14. item_quartz_cluster.glb       — 5'li Altigen Prizmatik Kuvars / Rezonans Kristali Salkimi
   15. item_metal_ingot.glb          — Pahli Trapez Dokum Metal Kulce
   16. item_natural_stick.glb        — Bogumlu & Yan Surgunlu Dogal Kivrimli Kuru Dal
   17. item_split_firewood.glb       — Disi Kabuklu, Ici Lifli Kama Yarma Odun / Kutuk
   18. item_meat_cut.glb             — Fildisi Kaburga Kemikli & Yag Seritli Kalin Et Pirzolasi
   19. item_wild_berries.glb         — Cift Yaprak Yataginda 7'li Yabani Bogurtlen Salkimi
   20. item_potion_bottle.glb        — Sekizgen Kristal Govdeli & Mantar Tipali Iksir Sisesi
   21. item_medical_bandage.glb      — Kirmizi Sifaci Muhurlu Keten Sargi Bezi Rulosu
   22. item_leather_bellows.glb      — Akordeon Deri Koruk & Bronz Ufleme Burnu
   23. item_clay_crucible.glb        — Dokum Gagali & Yan Kulplu Refrakter Seramik Pota
   24. item_martial_scroll.glb       — Altin Topuzlu & Mum Muhurlu Dojo Dovus Parsomeni
"""

from __future__ import annotations

import json
import math
import shutil
import struct
from pathlib import Path
from typing import Any

import bmesh  # type: ignore[import-not-found]
import bpy  # type: ignore[import-not-found]
from mathutils import Euler, Matrix, Vector  # type: ignore[import-not-found]

SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parents[2]
OUT_ITEMS_DIR = REPO_ROOT / "Assets" / "Art" / "Generated" / "Items"
OUT_WEAPONS_MOD_DIR = REPO_ROOT / "Assets" / "Art" / "Generated" / "Weapons_Modular"

# Stratocraft MaterialIdCatalog sabitleri (StratocraftUberShader R=MaterialID, G=Temp, B=Sharpness, A=AO)
MAT_STONE = 1
MAT_DIRT = 2
MAT_GRASS = 3
MAT_SAND = 4
MAT_GLOWING_MOSS = 5
MAT_BASALT = 6
MAT_COBBLESTONE = 7
MAT_MARBLE = 8
MAT_CLAY = 10
MAT_MAGMA = 13
MAT_COAL = 30
MAT_COPPER_ORE = 31
MAT_IRON_ORE = 32
MAT_GOLD_ORE = 35
MAT_MITHRIL_ORE = 37
MAT_RUBY_GEM = 39
MAT_QUARTZ_GEM = 40
MAT_ASTRAL_ORE = 41
MAT_RED_DIAMOND = 42
MAT_BRONZE = 50
MAT_STEEL = 51
MAT_DAMASCUS_STEEL = 60
MAT_NATURAL_OBSIDIAN = 61
MAT_OAK_WOOD = 70
MAT_IRONWOOD = 71
MAT_WOOD_PLANK = 72
MAT_THATCH = 73
MAT_TACTICAL_CLOTH = 74
MAT_LEATHER = 75
MAT_BONE_IVORY = 76
MAT_FUR_ASH = 77


def _reset_scene() -> None:
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for mesh in list(bpy.data.meshes):
        bpy.data.meshes.remove(mesh, do_unlink=True)


def _new_rgba_layer(bm: bmesh.types.BMesh):
    layer = bm.faces.layers.int.get("sc_rgba")
    if layer is None:
        layer = bm.faces.layers.int.new("sc_rgba")
    return layer


def _pack_rgba(r: int, g: int, b: int, a: int) -> int:
    return ((r & 0xFF) << 24) | ((g & 0xFF) << 16) | ((b & 0xFF) << 8) | (a & 0xFF)


def _unpack_rgba(packed: int) -> tuple[int, int, int, int]:
    return (
        (packed >> 24) & 0xFF,
        (packed >> 16) & 0xFF,
        (packed >> 8) & 0xFF,
        packed & 0xFF,
    )


def _add_lofted_tube(
    bm: bmesh.types.BMesh,
    layer: Any,
    stations: list[tuple[tuple[float, float, float], tuple[float, float], float]],
    sides: int,
    rgba: tuple[int, int, int, int],
    cap_bottom: bool = True,
    cap_top: bool = True,
    twist_rad: float = 0.0,
) -> None:
    if len(stations) < 2 or sides < 3:
        return
    packed = _pack_rgba(*rgba)
    rings: list[list[bmesh.types.BMVert]] = []

    for s_idx, (center, radii, tilt_x) in enumerate(stations):
        cx, cy, cz = center
        rx, rz = radii
        rot_mat = Matrix.Rotation(tilt_x, 3, "X")
        ang_offset = twist_rad * (s_idx / max(1, len(stations) - 1)) + (math.pi / sides)
        ring_verts: list[bmesh.types.BMVert] = []
        for i in range(sides):
            ang = (2.0 * math.pi * i / sides) + ang_offset
            local_v = Vector((math.cos(ang) * rx, 0.0, math.sin(ang) * rz))
            world_v = rot_mat @ local_v + Vector((cx, cy, cz))
            ring_verts.append(bm.verts.new((world_v.x, world_v.y, world_v.z)))
        rings.append(ring_verts)

    for r_idx in range(len(rings) - 1):
        r0, r1 = rings[r_idx], rings[r_idx + 1]
        for i in range(sides):
            ni = (i + 1) % sides
            try:
                f = bm.faces.new((r0[i], r0[ni], r1[ni], r1[i]))
                f[layer] = packed
            except ValueError:
                pass

    if cap_bottom:
        v_bot = bm.verts.new(stations[0][0])
        for i in range(sides):
            ni = (i + 1) % sides
            try:
                f = bm.faces.new((v_bot, rings[0][ni], rings[0][i]))
                f[layer] = packed
            except ValueError:
                pass

    if cap_top:
        v_top = bm.verts.new(stations[-1][0])
        top_ring = rings[-1]
        for i in range(sides):
            ni = (i + 1) % sides
            try:
                f = bm.faces.new((v_top, top_ring[i], top_ring[ni]))
                f[layer] = packed
            except ValueError:
                pass


def _add_spine_limb(
    bm: bmesh.types.BMesh,
    layer: Any,
    spine_pts: list[tuple[tuple[float, float, float], float, float]],
    sides: int,
    rgba: tuple[int, int, int, int],
    cap_start: bool = True,
    cap_end: bool = True,
) -> None:
    if len(spine_pts) < 2:
        return
    packed = _pack_rgba(*rgba)
    rings: list[list[bmesh.types.BMVert]] = []

    for idx, (pt, rx, ry) in enumerate(spine_pts):
        p_cur = Vector(pt)
        if idx == 0:
            tangent = (Vector(spine_pts[1][0]) - p_cur).normalized()
        elif idx == len(spine_pts) - 1:
            tangent = (p_cur - Vector(spine_pts[idx - 1][0])).normalized()
        else:
            tangent = (Vector(spine_pts[idx + 1][0]) - Vector(spine_pts[idx - 1][0])).normalized()
        if tangent.length < 1e-6:
            tangent = Vector((0.0, 1.0, 0.0))

        up_ref = Vector((0.0, 1.0, 0.0)) if abs(tangent.y) < 0.92 else Vector((1.0, 0.0, 0.0))
        side_axis = tangent.cross(up_ref).normalized()
        binorm_axis = side_axis.cross(tangent).normalized()

        ring: list[bmesh.types.BMVert] = []
        for i in range(sides):
            ang = 2.0 * math.pi * i / sides + (math.pi / sides)
            offset = side_axis * (math.cos(ang) * rx) + binorm_axis * (math.sin(ang) * ry)
            pos = p_cur + offset
            ring.append(bm.verts.new((pos.x, pos.y, pos.z)))
        rings.append(ring)

    for r_idx in range(len(rings) - 1):
        r0, r1 = rings[r_idx], rings[r_idx + 1]
        for i in range(sides):
            ni = (i + 1) % sides
            try:
                f = bm.faces.new((r0[i], r0[ni], r1[ni], r1[i]))
                f[layer] = packed
            except ValueError:
                pass

    if cap_start:
        v_s = bm.verts.new(spine_pts[0][0])
        for i in range(sides):
            ni = (i + 1) % sides
            try:
                f = bm.faces.new((v_s, rings[0][ni], rings[0][i]))
                f[layer] = packed
            except ValueError:
                pass

    if cap_end:
        v_e = bm.verts.new(spine_pts[-1][0])
        for i in range(sides):
            ni = (i + 1) % sides
            try:
                f = bm.faces.new((v_e, rings[-1][i], rings[-1][ni]))
                f[layer] = packed
            except ValueError:
                pass


def _add_chamfered_box(
    bm: bmesh.types.BMesh,
    layer: Any,
    center: tuple[float, float, float],
    size: tuple[float, float, float],
    rgba: tuple[int, int, int, int],
    bevel_amt: float = 0.12,
    rot_euler: tuple[float, float, float] = (0.0, 0.0, 0.0),
) -> None:
    packed = _pack_rgba(*rgba)
    ret = bmesh.ops.create_cube(bm, size=1.0)
    verts = ret["verts"]
    sx, sy, sz = size
    for v in verts:
        v.co.x *= sx
        v.co.y *= sy
        v.co.z *= sz

    min_dim = min(sx, sy, sz)
    if bevel_amt > 0.001 and min_dim > 0.003:
        geom_edges = list({e for v in verts for e in v.link_edges})
        bev = bmesh.ops.bevel(
            bm,
            geom=list(verts) + geom_edges,
            offset=min_dim * bevel_amt,
            offset_type="OFFSET",
            segments=1,
            profile=0.5,
            affect="EDGES",
        )
        all_v = list(bev["verts"])
    else:
        all_v = list(verts)

    rot_m = Euler(rot_euler, "XYZ").to_matrix()
    c_vec = Vector(center)
    visited = set()
    for v in all_v:
        v.co = (rot_m @ v.co) + c_vec
        for f in v.link_faces:
            visited.add(f)
    for f in visited:
        if f[layer] == 0:
            f[layer] = packed


def _add_deformed_rock_blob(
    bm: bmesh.types.BMesh,
    layer: Any,
    center: tuple[float, float, float],
    radii: tuple[float, float, float],
    rgba: tuple[int, int, int, int],
    seed: int = 1337,
    flatten_bottom: float = 0.45,
    subdivisions: int = 1,
) -> None:
    packed = _pack_rgba(*rgba)
    ret = bmesh.ops.create_icosphere(bm, subdivisions=subdivisions, radius=1.0)
    verts = ret["verts"]
    rx, ry, rz = radii
    cx, cy, cz = center
    visited = set()
    for idx, v in enumerate(verts):
        h = ((seed * 1103515245 + idx * 12345) & 0x7FFFFFFF) % 1000 / 1000.0
        jitter = 0.84 + h * 0.32
        vx = v.co.x * rx * jitter
        vy = v.co.y * ry * jitter
        vz = v.co.z * rz * jitter
        if vy < 0.0 and flatten_bottom > 0.0:
            vy *= 1.0 - flatten_bottom * 0.65
        v.co = Vector((cx + vx, cy + vy, cz + vz))
        for f in v.link_faces:
            visited.add(f)
    for f in visited:
        if f[layer] == 0:
            f[layer] = packed


# ==============================================================================
# 24 YUKSEK DETAYLI 3B ESYA / ALET / MALZEME HEYKEL FONKSIYONLARI
# ==============================================================================


def _sculpt_tool_axe_iron(bm: bmesh.types.BMesh, layer: Any) -> None:
    """Sakalli Celik Oduncu/Savas Baltasi (0.36 x 0.68 x 0.07m)."""
    # Ergonomik disbudak sap + alt topuz + deri kavrama sargisi
    _add_lofted_tube(
        bm,
        layer,
        [
            ((0.0, 0.00, 0.0), (0.026, 0.019), 0.0),
            ((0.0, 0.05, 0.0), (0.019, 0.014), 0.0),
            ((0.0, 0.14, 0.0), (0.021, 0.016), 0.0),
            ((0.0, 0.36, 0.0), (0.020, 0.015), 0.0),
            ((0.0, 0.54, 0.0), (0.024, 0.018), 0.0),
            ((0.0, 0.68, 0.0), (0.027, 0.020), 0.0),
        ],
        sides=10,
        rgba=(MAT_OAK_WOOD, 0, 172, 242),
    )
    # Alt ve orta deri kavrama halkalari
    for y_grip in (0.13, 0.16, 0.28, 0.31):
        _add_lofted_tube(
            bm,
            layer,
            [((0.0, y_grip, 0.0), (0.023, 0.018), 0.0), ((0.0, y_grip + 0.022, 0.0), (0.023, 0.018), 0.0)],
            sides=8,
            rgba=(MAT_LEATHER, 0, 138, 232),
        )
    # Celik goz bilezigi (eye collar) + arka denge cekici (poll)
    _add_chamfered_box(
        bm, layer, (-0.015, 0.565, 0.0), (0.095, 0.095, 0.052), (MAT_STEEL, 0, 228, 246), bevel_amt=0.18
    )
    _add_chamfered_box(
        bm, layer, (-0.072, 0.565, 0.0), (0.048, 0.074, 0.044), (MAT_STEEL, 0, 215, 240), bevel_amt=0.20
    )
    # Genisleyen sakalli (bearded) balta yanagi
    _add_chamfered_box(
        bm, layer, (0.075, 0.555, 0.0), (0.130, 0.145, 0.030), (MAT_STEEL, 0, 238, 250), bevel_amt=0.16
    )
    _add_chamfered_box(
        bm, layer, (0.115, 0.495, 0.0), (0.085, 0.095, 0.022), (MAT_STEEL, 0, 242, 250), bevel_amt=0.16
    )
    # Jilet keskinliginde parlak kavisli celik agiz (bevel edge - yanakla tam birlesik!)
    _add_spine_limb(
        bm,
        layer,
        [
            ((0.128, 0.640, 0.0), 0.014, 0.015),
            ((0.152, 0.585, 0.0), 0.016, 0.016),
            ((0.156, 0.515, 0.0), 0.016, 0.016),
            ((0.138, 0.448, 0.0), 0.012, 0.014),
        ],
        sides=6,
        rgba=(MAT_DAMASCUS_STEEL, 0, 254, 255),
    )


def _sculpt_tool_primitive_axe(bm: bmesh.types.BMesh, layer: Any) -> None:
    """Lif Sargili Yontulmus Cakmaktasi Ilkel Balta (0.30 x 0.60 x 0.07m)."""
    _add_spine_limb(
        bm,
        layer,
        [
            ((0.00, 0.00, 0.00), 0.024, 0.020),
            ((0.01, 0.18, 0.00), 0.021, 0.019),
            ((-0.01, 0.38, 0.00), 0.023, 0.020),
            ((0.00, 0.60, 0.00), 0.026, 0.022),
        ],
        sides=8,
        rgba=(MAT_OAK_WOOD, 0, 162, 238),
    )
    # Capraz bitki lifi sargi dugumu
    for pitch in (-0.38, 0.38):
        _add_chamfered_box(
            bm,
            layer,
            (0.01, 0.49, 0.0),
            (0.072, 0.078, 0.064),
            (MAT_GLOWING_MOSS, 0, 148, 228),
            bevel_amt=0.22,
            rot_euler=(0.0, 0.0, pitch),
        )
    # Yontulmus fasetli cakmaktasi balta basi
    _add_chamfered_box(
        bm, layer, (0.065, 0.49, 0.0), (0.135, 0.105, 0.038), (MAT_BASALT, 0, 225, 245), bevel_amt=0.24
    )
    _add_spine_limb(
        bm,
        layer,
        [
            ((0.125, 0.555, 0.0), 0.007, 0.012),
            ((0.145, 0.490, 0.0), 0.009, 0.015),
            ((0.120, 0.425, 0.0), 0.007, 0.012),
        ],
        sides=5,
        rgba=(MAT_NATURAL_OBSIDIAN, 0, 248, 252),
    )


def _sculpt_tool_stone_hammer(bm: bmesh.types.BMesh, layer: Any) -> None:
    """Bogumlu Sapli & Lif Sargili Agir Tas/Demir Cekic (0.24 x 0.56 x 0.12m)."""
    _add_lofted_tube(
        bm,
        layer,
        [
            ((0.0, 0.00, 0.0), (0.025, 0.021), 0.0),
            ((0.0, 0.08, 0.0), (0.019, 0.017), 0.0),
            ((0.0, 0.38, 0.0), (0.022, 0.019), 0.0),
            ((0.0, 0.56, 0.0), (0.026, 0.022), 0.0),
        ],
        sides=8,
        rgba=(MAT_OAK_WOOD, 0, 168, 240),
    )
    # Capraz lif/deri baglama kusagi
    _add_chamfered_box(
        bm, layer, (0.0, 0.46, 0.0), (0.075, 0.085, 0.075), (MAT_GLOWING_MOSS, 0, 152, 232), bevel_amt=0.22
    )
    # Sekizgen pahli agir tas tokmak basi + iki yan vurus yuzeyi
    _add_chamfered_box(
        bm, layer, (0.0, 0.46, 0.0), (0.20, 0.105, 0.105), (MAT_COBBLESTONE, 0, 210, 244), bevel_amt=0.22
    )
    for sign in (-1.0, 1.0):
        _add_chamfered_box(
            bm,
            layer,
            (sign * 0.105, 0.46, 0.0),
            (0.032, 0.088, 0.088),
            (MAT_STONE, 0, 232, 250),
            bevel_amt=0.20,
        )


def _sculpt_tool_primitive_knife(bm: bmesh.types.BMesh, layer: Any) -> None:
    """Kemik/Ahsap Kabzali Yontulmus Cakmaktasi Av Bicagi (0.08 x 0.36 x 0.04m)."""
    # Ergonomik kemik/ahsap kabza (y = 0.00 .. 0.14)
    _add_lofted_tube(
        bm,
        layer,
        [
            ((0.0, 0.00, 0.0), (0.018, 0.014), 0.0),
            ((0.0, 0.03, 0.0), (0.015, 0.012), 0.0),
            ((0.0, 0.09, 0.0), (0.018, 0.014), 0.0),
            ((0.0, 0.14, 0.0), (0.020, 0.015), 0.0),
        ],
        sides=8,
        rgba=(MAT_OAK_WOOD, 0, 168, 240),
    )
    # Bitki lifi bogum bilezigi (y = 0.125 .. 0.165)
    _add_lofted_tube(
        bm,
        layer,
        [((0.0, 0.125, 0.0), (0.024, 0.019), 0.0), ((0.0, 0.165, 0.0), (0.023, 0.018), 0.0)],
        sides=8,
        rgba=(MAT_GLOWING_MOSS, 0, 150, 232),
    )
    # Yontulmus fasetli cakmaktasi/obsidyen namlu (y = 0.155 .. 0.360)
    _add_lofted_tube(
        bm,
        layer,
        [
            ((0.000, 0.155, 0.0), (0.026, 0.008), 0.0),
            ((0.004, 0.235, 0.0), (0.028, 0.007), 0.0),
            ((0.002, 0.305, 0.0), (0.019, 0.005), 0.0),
            ((-0.004, 0.360, 0.0), (0.002, 0.002), 0.0),
        ],
        sides=6,
        rgba=(MAT_BASALT, 0, 238, 250),
    )


def _sculpt_tool_micro_chisel(bm: bmesh.types.BMesh, layer: Any) -> None:
    """Pirinc Yuksuklu & Jilet Pahli 4x4x4 Mimari Mikro-Keski (0.06 x 0.30 x 0.06m)."""
    # Ust darbe halkasi + sekizgen demiragaci sap + alt pirinc yuksuk
    _add_lofted_tube(
        bm,
        layer,
        [
            ((0.0, 0.00, 0.0), (0.018, 0.018), 0.0),
            ((0.0, 0.02, 0.0), (0.022, 0.022), 0.0),
            ((0.0, 0.11, 0.0), (0.024, 0.024), 0.0),
            ((0.0, 0.15, 0.0), (0.018, 0.018), 0.0),
        ],
        sides=8,
        rgba=(MAT_IRONWOOD, 0, 175, 242),
    )
    for y_ring in (0.00, 0.135):
        _add_lofted_tube(
            bm,
            layer,
            [((0.0, y_ring, 0.0), (0.023, 0.023), 0.0), ((0.0, y_ring + 0.020, 0.0), (0.023, 0.023), 0.0)],
            sides=8,
            rgba=(MAT_BRONZE, 0, 230, 248),
        )
    # Celik keski boynu ve genisleyen pahli yontma agzi
    _add_lofted_tube(
        bm,
        layer,
        [
            ((0.0, 0.155, 0.0), (0.012, 0.012), 0.0),
            ((0.0, 0.235, 0.0), (0.016, 0.008), 0.0),
            ((0.0, 0.285, 0.0), (0.026, 0.005), 0.0),
            ((0.0, 0.300, 0.0), (0.028, 0.0015), 0.0),
        ],
        sides=6,
        rgba=(MAT_DAMASCUS_STEEL, 0, 250, 255),
    )


def _sculpt_tool_shovel_spade(bm: bmesh.types.BMesh, layer: Any) -> None:
    """T-Kabzali & Perçinli Celik Kurek (0.24 x 0.78 x 0.07m)."""
    # Ust T-kabza
    _add_chamfered_box(
        bm, layer, (0.0, 0.025, 0.0), (0.13, 0.036, 0.036), (MAT_OAK_WOOD, 0, 172, 242), bevel_amt=0.22
    )
    # Ana ahsap sap + celik bogaz soketi
    _add_lofted_tube(
        bm,
        layer,
        [
            ((0.0, 0.03, 0.0), (0.018, 0.018), 0.0),
            ((0.0, 0.46, 0.0), (0.020, 0.020), 0.0),
            ((0.0, 0.56, 0.0), (0.023, 0.022), 0.0),
        ],
        sides=8,
        rgba=(MAT_OAK_WOOD, 0, 168, 240),
    )
    _add_lofted_tube(
        bm,
        layer,
        [((0.0, 0.48, 0.0), (0.024, 0.024), 0.0), ((0.0, 0.58, 0.0), (0.026, 0.022), 0.0)],
        sides=8,
        rgba=(MAT_STEEL, 0, 225, 246),
    )
    # Sivri uclu celik kurek kepcesi
    _add_lofted_tube(
        bm,
        layer,
        [
            ((0.0, 0.56, 0.00), (0.115, 0.018), 0.0),
            ((0.0, 0.68, 0.01), (0.108, 0.016), 0.0),
            ((0.0, 0.75, 0.00), (0.065, 0.010), 0.0),
            ((0.0, 0.78, 0.00), (0.008, 0.003), 0.0),
        ],
        sides=6,
        rgba=(MAT_DAMASCUS_STEEL, 0, 244, 252),
    )


def _sculpt_torch_campfire(bm: bmesh.types.BMesh, layer: Any) -> None:
    """Recineli Bez Sargili & T^4 Kor Alevli Mesale / Kamp Atesi (0.16 x 0.48 x 0.16m)."""
    # Budakli demiragaci mesale govdesi
    _add_lofted_tube(
        bm,
        layer,
        [
            ((0.0, 0.00, 0.0), (0.018, 0.018), 0.0),
            ((0.0, 0.14, 0.0), (0.022, 0.022), 0.0),
            ((0.0, 0.30, 0.0), (0.028, 0.028), 0.0),
            ((0.0, 0.36, 0.0), (0.035, 0.035), 0.0),
        ],
        sides=8,
        rgba=(MAT_IRONWOOD, 0, 165, 238),
    )
    # Demir kafes tutucu halka + recineli bez sargi
    _add_lofted_tube(
        bm,
        layer,
        [((0.0, 0.28, 0.0), (0.038, 0.038), 0.0), ((0.0, 0.31, 0.0), (0.042, 0.042), 0.0)],
        sides=8,
        rgba=(MAT_STEEL, 0, 228, 246),
    )
    _add_lofted_tube(
        bm,
        layer,
        [((0.0, 0.31, 0.0), (0.045, 0.045), 0.0), ((0.0, 0.39, 0.0), (0.048, 0.048), 0.0)],
        sides=8,
        rgba=(MAT_TACTICAL_CLOTH, 45, 160, 235),
    )
    # T^4 Stefan-Boltzmann Kor + Alev Tac Basligi (G=238 yuksek sicaklik!)
    _add_lofted_tube(
        bm,
        layer,
        [
            ((0.0, 0.37, 0.0), (0.052, 0.052), 0.0),
            ((0.0, 0.43, 0.0), (0.038, 0.038), 0.0),
            ((0.0, 0.48, 0.0), (0.006, 0.006), 0.0),
        ],
        sides=7,
        rgba=(MAT_MAGMA, 238, 245, 255),
        twist_rad=0.45,
    )


def _sculpt_fluid_flask(bm: bmesh.types.BMesh, layer: Any) -> None:
    """Deri Askili, Pirinc Boyunlu & Mantar Tipali Sivi Matarasi (0.16 x 0.24 x 0.11m)."""
    # Siskin yassi matara govdesi
    _add_lofted_tube(
        bm,
        layer,
        [
            ((0.0, 0.00, 0.0), (0.048, 0.032), 0.0),
            ((0.0, 0.04, 0.0), (0.075, 0.048), 0.0),
            ((0.0, 0.11, 0.0), (0.078, 0.050), 0.0),
            ((0.0, 0.16, 0.0), (0.055, 0.036), 0.0),
            ((0.0, 0.185, 0.0), (0.024, 0.024), 0.0),
        ],
        sides=10,
        rgba=(MAT_ASTRAL_ORE, 0, 225, 246),
    )
    # Bel deri kusagi + capraz aski kayisi
    _add_lofted_tube(
        bm,
        layer,
        [((0.0, 0.065, 0.0), (0.081, 0.053), 0.0), ((0.0, 0.115, 0.0), (0.081, 0.053), 0.0)],
        sides=10,
        rgba=(MAT_LEATHER, 0, 152, 236),
    )
    # Pirinc boyun bilezigi + mantar tipa
    _add_lofted_tube(
        bm,
        layer,
        [((0.0, 0.182, 0.0), (0.027, 0.027), 0.0), ((0.0, 0.212, 0.0), (0.025, 0.025), 0.0)],
        sides=8,
        rgba=(MAT_BRONZE, 0, 235, 250),
    )
    _add_lofted_tube(
        bm,
        layer,
        [((0.0, 0.210, 0.0), (0.020, 0.020), 0.0), ((0.0, 0.240, 0.0), (0.023, 0.023), 0.0)],
        sides=8,
        rgba=(MAT_OAK_WOOD, 0, 165, 240),
    )


def _sculpt_fluid_bucket(bm: bmesh.types.BMesh, layer: Any) -> None:
    """Cift Demir Cemberli & Kavisli Kulplu Ahsap Fici Kova (0.28 x 0.30 x 0.28m)."""
    # Dis fici govdesi (ust kapaksiz, ic sivi gorunur!)
    _add_lofted_tube(
        bm,
        layer,
        [
            ((0.0, 0.00, 0.0), (0.102, 0.102), 0.0),
            ((0.0, 0.11, 0.0), (0.118, 0.118), 0.0),
            ((0.0, 0.22, 0.0), (0.132, 0.132), 0.0),
        ],
        sides=10,
        rgba=(MAT_OAK_WOOD, 0, 172, 242),
        cap_top=False,
    )
    # Alt ve ust dovme demir cemberler (dis yuzeyden +5mm disa cikintili)
    for y_hoop, r_hoop in ((0.035, 0.115), (0.172, 0.136)):
        _add_lofted_tube(
            bm,
            layer,
            [
                ((0.0, y_hoop, 0.0), (r_hoop, r_hoop), 0.0),
                ((0.0, y_hoop + 0.024, 0.0), (r_hoop + 0.003, r_hoop + 0.003), 0.0),
            ],
            sides=10,
            rgba=(MAT_STEEL, 0, 232, 248),
            cap_bottom=False,
            cap_top=False,
        )
    # Ic sivi yuzeyi (yalnizca ust agizda, yan duvarlardan asla tasmaz!)
    _add_lofted_tube(
        bm,
        layer,
        [((0.0, 0.185, 0.0), (0.104, 0.104), 0.0), ((0.0, 0.205, 0.0), (0.112, 0.112), 0.0)],
        sides=10,
        rgba=(MAT_ASTRAL_ORE, 0, 240, 252),
    )
    # Kavisli demir tasima kulpu (handle arch)
    _add_spine_limb(
        bm,
        layer,
        [
            ((-0.132, 0.21, 0.0), 0.010, 0.010),
            ((-0.110, 0.27, 0.0), 0.009, 0.009),
            ((0.000, 0.30, 0.0), 0.011, 0.011),
            ((0.110, 0.27, 0.0), 0.009, 0.009),
            ((0.132, 0.21, 0.0), 0.010, 0.010),
        ],
        sides=6,
        rgba=(MAT_STEEL, 0, 238, 250),
    )


def _sculpt_fluid_cauldron(bm: bmesh.types.BMesh, layer: Any) -> None:
    """3 Ayakli, Kulplu & Disa Kivrik Agizli Dokum Kazan (0.36 x 0.30 x 0.36m)."""
    # 3 dokum pence ayak
    for i in range(3):
        ang = i * (2.0 * math.pi / 3.0)
        ca, sa = math.cos(ang), math.sin(ang)
        _add_spine_limb(
            bm,
            layer,
            [
                ((ca * 0.11, 0.06, sa * 0.11), 0.024, 0.024),
                ((ca * 0.14, 0.00, sa * 0.14), 0.016, 0.016),
            ],
            sides=6,
            rgba=(MAT_BRONZE, 0, 220, 242),
        )
    # Siskin kazan govdesi + disa kivrik agiz (rolled rim)
    _add_lofted_tube(
        bm,
        layer,
        [
            ((0.0, 0.03, 0.0), (0.095, 0.095), 0.0),
            ((0.0, 0.11, 0.0), (0.168, 0.168), 0.0),
            ((0.0, 0.20, 0.0), (0.162, 0.162), 0.0),
            ((0.0, 0.25, 0.0), (0.138, 0.138), 0.0),
            ((0.0, 0.285, 0.0), (0.165, 0.165), 0.0),
            ((0.0, 0.300, 0.0), (0.155, 0.155), 0.0),
        ],
        sides=12,
        rgba=(MAT_BRONZE, 0, 232, 248),
    )
    # Yan tasima kulplari
    for sign in (-1.0, 1.0):
        _add_spine_limb(
            bm,
            layer,
            [
                ((sign * 0.155, 0.24, -0.035), 0.011, 0.011),
                ((sign * 0.180, 0.21, 0.000), 0.011, 0.011),
                ((sign * 0.155, 0.24, 0.035), 0.011, 0.011),
            ],
            sides=6,
            rgba=(MAT_STEEL, 0, 238, 250),
        )


def _sculpt_flint_stone(bm: bmesh.types.BMesh, layer: Any) -> None:
    """Konkoidal Kirikli & Keskin Sirtli Cakmaktasi Cekirdegi (0.15 x 0.09 x 0.20m)."""
    _add_lofted_tube(
        bm,
        layer,
        [
            ((0.00, 0.00, -0.085), (0.032, 0.018), 0.15),
            ((0.01, 0.03, -0.035), (0.068, 0.038), 0.05),
            ((-0.01, 0.05, 0.025), (0.072, 0.042), -0.05),
            ((0.00, 0.04, 0.075), (0.042, 0.024), -0.12),
            ((0.00, 0.02, 0.100), (0.005, 0.004), 0.0),
        ],
        sides=6,
        rgba=(MAT_BASALT, 0, 238, 248),
    )
    # Ust keskin konkoidal sirt faseti
    _add_spine_limb(
        bm,
        layer,
        [
            ((0.00, 0.045, -0.075), 0.014, 0.010),
            ((0.00, 0.082, 0.000), 0.026, 0.018),
            ((0.00, 0.045, 0.085), 0.008, 0.006),
        ],
        sides=5,
        rgba=(MAT_NATURAL_OBSIDIAN, 0, 250, 254),
    )


def _sculpt_rough_stone(bm: bmesh.types.BMesh, layer: Any) -> None:
    """Cok Yuzeyli Dogal Nehir/Kaya Tasi (0.24 x 0.16 x 0.22m)."""
    _add_deformed_rock_blob(
        bm,
        layer,
        (0.0, 0.075, 0.0),
        (0.115, 0.078, 0.105),
        (MAT_STONE, 0, 195, 242),
        seed=4099,
        flatten_bottom=0.65,
        subdivisions=1,
    )
    _add_deformed_rock_blob(
        bm,
        layer,
        (0.035, 0.095, -0.020),
        (0.068, 0.055, 0.062),
        (MAT_COBBLESTONE, 0, 212, 246),
        seed=7123,
        flatten_bottom=0.35,
        subdivisions=1,
    )


def _sculpt_ore_chunk(bm: bmesh.types.BMesh, layer: Any) -> None:
    """Kristal/Metalik Damarli Zengin Cevher Kayasi (0.25 x 0.18 x 0.23m)."""
    _add_deformed_rock_blob(
        bm,
        layer,
        (0.0, 0.08, 0.0),
        (0.115, 0.080, 0.105),
        (MAT_STONE, 0, 185, 238),
        seed=8821,
        flatten_bottom=0.65,
        subdivisions=1,
    )
    # Ana kayadan disari fiskiran 6 adet fasetli metalik cevher yumrusu
    vein_offsets = [
        (0.055, 0.115, 0.035, (0.044, 0.038, 0.040)),
        (-0.050, 0.110, 0.040, (0.042, 0.036, 0.038)),
        (0.015, 0.135, -0.035, (0.046, 0.042, 0.042)),
        (-0.045, 0.085, -0.050, (0.038, 0.034, 0.036)),
        (0.065, 0.080, -0.030, (0.036, 0.032, 0.034)),
        (0.000, 0.125, 0.055, (0.040, 0.035, 0.036)),
    ]
    for idx, (vx, vy, vz, vr) in enumerate(vein_offsets):
        _add_chamfered_box(
            bm,
            layer,
            (vx, vy, vz),
            (vr[0] * 1.7, vr[1] * 1.7, vr[2] * 1.7),
            (MAT_COPPER_ORE, 0, 242, 252),
            bevel_amt=0.26,
            rot_euler=(idx * 0.45, idx * 0.72, idx * 0.33),
        )


def _sculpt_quartz_cluster(bm: bmesh.types.BMesh, layer: Any) -> None:
    """5'li Altigen Prizmatik Kuvars / Rezonans Kristali Salkimi (0.20 x 0.24 x 0.20m)."""
    _add_deformed_rock_blob(
        bm,
        layer,
        (0.0, 0.03, 0.0),
        (0.082, 0.034, 0.078),
        (MAT_STONE, 0, 180, 235),
        seed=5501,
        flatten_bottom=0.80,
        subdivisions=1,
    )
    spires = [
        ((0.00, 0.02, 0.00), (0.00, 0.24, 0.00), 0.036),
        ((0.03, 0.02, 0.02), (0.075, 0.18, 0.055), 0.026),
        ((-0.03, 0.02, 0.02), (-0.075, 0.17, 0.050), 0.025),
        ((0.02, 0.02, -0.03), (0.065, 0.16, -0.070), 0.024),
        ((-0.02, 0.02, -0.03), (-0.065, 0.19, -0.060), 0.026),
    ]
    for p_base, p_tip, rad in spires:
        mid = (
            p_base[0] + (p_tip[0] - p_base[0]) * 0.74,
            p_base[1] + (p_tip[1] - p_base[1]) * 0.74,
            p_base[2] + (p_tip[2] - p_base[2]) * 0.74,
        )
        _add_spine_limb(
            bm,
            layer,
            [(p_base, rad, rad), (mid, rad * 0.90, rad * 0.90), (p_tip, 0.002, 0.002)],
            sides=6,
            rgba=(MAT_QUARTZ_GEM, 25, 250, 255),
        )


def _sculpt_metal_ingot(bm: bmesh.types.BMesh, layer: Any) -> None:
    """Pahli Trapez Dokum Metal Kulce (0.11 x 0.06 x 0.26m)."""
    _add_lofted_tube(
        bm,
        layer,
        [
            ((0.0, 0.00, 0.0), (0.055, 0.130), 0.0),
            ((0.0, 0.048, 0.0), (0.042, 0.112), 0.0),
            ((0.0, 0.058, 0.0), (0.035, 0.102), 0.0),
        ],
        sides=4,
        rgba=(MAT_STEEL, 0, 238, 250),
        twist_rad=math.pi * 0.25,
    )
    _add_chamfered_box(
        bm, layer, (0.0, 0.030, 0.0), (0.095, 0.052, 0.235), (MAT_STEEL, 0, 245, 252), bevel_amt=0.24
    )


def _sculpt_natural_stick(bm: bmesh.types.BMesh, layer: Any) -> None:
    """Bogumlu & Yan Surgunlu Dogal Kivrimli Kuru Dal (0.14 x 0.08 x 0.52m)."""
    _add_spine_limb(
        bm,
        layer,
        [
            ((-0.015, 0.030, -0.25), 0.025, 0.023),
            ((0.022, 0.042, -0.08), 0.022, 0.021),
            ((-0.014, 0.035, 0.10), 0.019, 0.018),
            ((0.012, 0.028, 0.25), 0.014, 0.014),
        ],
        sides=7,
        rgba=(MAT_OAK_WOOD, 0, 168, 240),
    )
    # Yan budak surgunu
    _add_spine_limb(
        bm,
        layer,
        [
            ((0.018, 0.040, -0.04), 0.014, 0.014),
            ((0.065, 0.058, 0.08), 0.007, 0.007),
        ],
        sides=6,
        rgba=(MAT_IRONWOOD, 0, 158, 235),
    )


def _sculpt_split_firewood(bm: bmesh.types.BMesh, layer: Any) -> None:
    """Disi Kabuklu, Ici Lifli Kama Yarma Odun / Kutuk (0.18 x 0.14 x 0.44m)."""
    # Alt dis kabuk sirti
    _add_chamfered_box(
        bm, layer, (0.0, 0.035, 0.0), (0.165, 0.055, 0.43), (MAT_OAK_WOOD, 0, 155, 235), bevel_amt=0.18
    )
    # Ust yarilmis ucgen oz odun kama sirti
    _add_chamfered_box(
        bm,
        layer,
        (0.0, 0.082, 0.0),
        (0.115, 0.075, 0.42),
        (MAT_WOOD_PLANK, 0, 195, 246),
        bevel_amt=0.22,
        rot_euler=(0.0, 0.0, math.pi * 0.25),
    )


def _sculpt_meat_cut(bm: bmesh.types.BMesh, layer: Any) -> None:
    """Fildisi Kaburga Kemikli & Yag Seritli Kalin Et Pirzolasi (0.22 x 0.08 x 0.17m)."""
    _add_deformed_rock_blob(
        bm,
        layer,
        (-0.015, 0.04, 0.0),
        (0.088, 0.038, 0.072),
        (MAT_RUBY_GEM, 0, 185, 242),
        seed=3109,
        flatten_bottom=0.55,
        subdivisions=1,
    )
    # Dis yag kemeri
    _add_chamfered_box(
        bm, layer, (-0.045, 0.042, 0.0), (0.095, 0.052, 0.115), (MAT_RUBY_GEM, 0, 192, 244), bevel_amt=0.26
    )
    # Fildisi kaburga/but kemigi
    _add_spine_limb(
        bm,
        layer,
        [
            ((0.02, 0.042, 0.00), 0.017, 0.015),
            ((0.095, 0.046, 0.01), 0.014, 0.013),
            ((0.115, 0.048, 0.015), 0.021, 0.018),
        ],
        sides=6,
        rgba=(MAT_BONE_IVORY, 0, 228, 250),
    )


def _sculpt_wild_berries(bm: bmesh.types.BMesh, layer: Any) -> None:
    """Cift Yaprak Yataginda 7'li Yabani Bogurtlen Salkimi (0.15 x 0.11 x 0.15m)."""
    # Yaprak tabani
    for yaw in (-0.45, 0.55):
        _add_chamfered_box(
            bm,
            layer,
            (0.0, 0.015, 0.0),
            (0.125, 0.018, 0.085),
            (MAT_GRASS, 0, 175, 238),
            bevel_amt=0.25,
            rot_euler=(0.0, yaw, 0.0),
        )
    berry_pts = [
        (0.00, 0.068, 0.00, 0.026),
        (0.032, 0.048, 0.018, 0.024),
        (-0.032, 0.048, 0.018, 0.024),
        (0.022, 0.048, -0.028, 0.023),
        (-0.024, 0.048, -0.026, 0.023),
        (0.00, 0.046, 0.036, 0.022),
    ]
    for idx, (bx, by, bz, br) in enumerate(berry_pts):
        _add_deformed_rock_blob(
            bm,
            layer,
            (bx, by, bz),
            (br, br * 0.95, br),
            (MAT_RED_DIAMOND, 15, 238, 250),
            seed=900 + idx,
            flatten_bottom=0.15,
            subdivisions=1,
        )


def _sculpt_potion_bottle(bm: bmesh.types.BMesh, layer: Any) -> None:
    """Sekizgen Kristal Govdeli & Mantar Tipali Iksir Sisesi (0.12 x 0.20 x 0.12m)."""
    _add_lofted_tube(
        bm,
        layer,
        [
            ((0.0, 0.00, 0.0), (0.038, 0.038), 0.0),
            ((0.0, 0.04, 0.0), (0.058, 0.058), 0.0),
            ((0.0, 0.10, 0.0), (0.056, 0.056), 0.0),
            ((0.0, 0.14, 0.0), (0.022, 0.022), 0.0),
            ((0.0, 0.17, 0.0), (0.024, 0.024), 0.0),
        ],
        sides=8,
        rgba=(MAT_RUBY_GEM, 35, 242, 252),
    )
    # Altin boyun bilezigi + mantar tipa
    _add_lofted_tube(
        bm,
        layer,
        [((0.0, 0.155, 0.0), (0.027, 0.027), 0.0), ((0.0, 0.175, 0.0), (0.028, 0.028), 0.0)],
        sides=8,
        rgba=(MAT_GOLD_ORE, 0, 245, 252),
    )
    _add_lofted_tube(
        bm,
        layer,
        [((0.0, 0.172, 0.0), (0.019, 0.019), 0.0), ((0.0, 0.200, 0.0), (0.022, 0.022), 0.0)],
        sides=8,
        rgba=(MAT_OAK_WOOD, 0, 170, 240),
    )


def _sculpt_medical_bandage(bm: bmesh.types.BMesh, layer: Any) -> None:
    """Kirmizi Sifaci Muhurlu Keten Sargi Bezi Rulosu (0.13 x 0.09 x 0.13m)."""
    _add_lofted_tube(
        bm,
        layer,
        [
            ((0.0, 0.00, 0.0), (0.056, 0.056), 0.0),
            ((0.0, 0.045, 0.0), (0.060, 0.060), 0.0),
            ((0.0, 0.090, 0.0), (0.056, 0.056), 0.0),
        ],
        sides=10,
        rgba=(MAT_TACTICAL_CLOTH, 0, 195, 246),
    )
    # Kirmizi sifaci kusagi
    _add_lofted_tube(
        bm,
        layer,
        [((0.0, 0.032, 0.0), (0.062, 0.062), 0.0), ((0.0, 0.058, 0.0), (0.062, 0.062), 0.0)],
        sides=10,
        rgba=(MAT_RUBY_GEM, 0, 225, 250),
    )


def _sculpt_leather_bellows(bm: bmesh.types.BMesh, layer: Any) -> None:
    """Akordeon Deri Koruk & Bronz Ufleme Burnu (0.20 x 0.12 x 0.36m)."""
    # Alt ve ust ahsap kanatlar + tutamaklar
    _add_chamfered_box(
        bm, layer, (0.0, 0.022, -0.03), (0.17, 0.020, 0.24), (MAT_OAK_WOOD, 0, 172, 242), bevel_amt=0.20
    )
    _add_chamfered_box(
        bm,
        layer,
        (0.0, 0.082, -0.03),
        (0.17, 0.020, 0.24),
        (MAT_OAK_WOOD, 0, 178, 244),
        bevel_amt=0.20,
        rot_euler=(0.16, 0.0, 0.0),
    )
    # Akordeon deri hazne
    _add_spine_limb(
        bm,
        layer,
        [
            ((0.0, 0.050, -0.13), 0.072, 0.032),
            ((0.0, 0.052, -0.04), 0.080, 0.038),
            ((0.0, 0.048, 0.06), 0.048, 0.024),
        ],
        sides=8,
        rgba=(MAT_LEATHER, 0, 150, 236),
    )
    # Konik bronz ufleme nozulu
    _add_spine_limb(
        bm,
        layer,
        [((0.0, 0.044, 0.07), 0.025, 0.025), ((0.0, 0.040, 0.18), 0.011, 0.011)],
        sides=8,
        rgba=(MAT_BRONZE, 0, 238, 250),
    )


def _sculpt_clay_crucible(bm: bmesh.types.BMesh, layer: Any) -> None:
    """Dokum Gagali & Yan Kulplu Refrakter Seramik Pota (0.22 x 0.20 x 0.22m)."""
    _add_lofted_tube(
        bm,
        layer,
        [
            ((0.0, 0.00, 0.0), (0.065, 0.065), 0.0),
            ((0.0, 0.08, 0.0), (0.102, 0.102), 0.0),
            ((0.0, 0.17, 0.0), (0.098, 0.105), 0.0),
            ((0.0, 0.20, 0.0), (0.088, 0.096), 0.0),
        ],
        sides=10,
        rgba=(MAT_CLAY, 0, 178, 242),
    )
    # On dokum gagasi (pouring spout)
    _add_chamfered_box(
        bm, layer, (0.0, 0.185, 0.095), (0.045, 0.030, 0.045), (MAT_CLAY, 0, 188, 245), bevel_amt=0.22
    )


def _sculpt_martial_scroll(bm: bmesh.types.BMesh, layer: Any) -> None:
    """Altin Topuzlu & Mum Muhurlu Dojo Dovus Parsomeni (0.14 x 0.42 x 0.14m)."""
    # Rulo parsomen govdesi
    _add_lofted_tube(
        bm,
        layer,
        [((0.0, 0.05, 0.0), (0.052, 0.052), 0.0), ((0.0, 0.37, 0.0), (0.052, 0.052), 0.0)],
        sides=10,
        rgba=(MAT_SAND, 0, 175, 244),
    )
    # Alt ve ust ahsap/altin makara uclari
    for y_end, y_tip in ((0.02, 0.00), (0.37, 0.40)):
        _add_lofted_tube(
            bm,
            layer,
            [((0.0, y_end, 0.0), (0.062, 0.062), 0.0), ((0.0, y_end + 0.028, 0.0), (0.062, 0.062), 0.0)],
            sides=10,
            rgba=(MAT_OAK_WOOD, 0, 180, 244),
        )
        _add_lofted_tube(
            bm,
            layer,
            [((0.0, y_tip, 0.0), (0.026, 0.026), 0.0), ((0.0, y_tip + 0.022, 0.0), (0.026, 0.026), 0.0)],
            sides=8,
            rgba=(MAT_GOLD_ORE, 0, 245, 252),
        )
    # Orta ipek kusak + kirmizi mum muhur
    _add_lofted_tube(
        bm,
        layer,
        [((0.0, 0.185, 0.0), (0.056, 0.056), 0.0), ((0.0, 0.235, 0.0), (0.056, 0.056), 0.0)],
        sides=10,
        rgba=(MAT_RUBY_GEM, 0, 232, 250),
    )
    _add_chamfered_box(
        bm, layer, (0.0, 0.21, 0.058), (0.044, 0.044, 0.018), (MAT_RUBY_GEM, 20, 248, 254), bevel_amt=0.25
    )


ITEM_CATALOG_SPECS: list[dict[str, Any]] = [
    {
        "asset_id": "item_tool_axe_iron",
        "dims": (0.36, 0.68, 0.07),
        "poly_budget": 420,
        "default_mat": MAT_STEEL,
        "sockets": ["Socket_Grip_Main", "Socket_Grip_Offhand", "Socket_VoxelBlade"],
        "fn": _sculpt_tool_axe_iron,
        "also_copy_to_weapons_mod": "tool_axe_iron.glb",
    },
    {
        "asset_id": "item_tool_primitive_axe",
        "dims": (0.30, 0.60, 0.07),
        "poly_budget": 380,
        "default_mat": MAT_BASALT,
        "sockets": ["Socket_Grip_Main", "Socket_Grip_Offhand"],
        "fn": _sculpt_tool_primitive_axe,
    },
    {
        "asset_id": "item_tool_stone_hammer",
        "dims": (0.24, 0.56, 0.12),
        "poly_budget": 380,
        "default_mat": MAT_COBBLESTONE,
        "sockets": ["Socket_Grip_Main", "Socket_Grip_Offhand"],
        "fn": _sculpt_tool_stone_hammer,
    },
    {
        "asset_id": "item_tool_primitive_knife",
        "dims": (0.08, 0.36, 0.04),
        "poly_budget": 320,
        "default_mat": MAT_BASALT,
        "sockets": ["Socket_Grip_Main"],
        "fn": _sculpt_tool_primitive_knife,
    },
    {
        "asset_id": "item_tool_micro_chisel",
        "dims": (0.06, 0.30, 0.06),
        "poly_budget": 320,
        "default_mat": MAT_STEEL,
        "sockets": ["Socket_Grip_Main"],
        "fn": _sculpt_tool_micro_chisel,
    },
    {
        "asset_id": "item_tool_shovel_spade",
        "dims": (0.24, 0.78, 0.07),
        "poly_budget": 380,
        "default_mat": MAT_STEEL,
        "sockets": ["Socket_Grip_Main", "Socket_Grip_Offhand"],
        "fn": _sculpt_tool_shovel_spade,
    },
    {
        "asset_id": "item_torch_campfire",
        "dims": (0.16, 0.48, 0.16),
        "poly_budget": 360,
        "default_mat": MAT_MAGMA,
        "sockets": ["Socket_Grip_Main"],
        "fn": _sculpt_torch_campfire,
    },
    {
        "asset_id": "item_fluid_flask",
        "dims": (0.16, 0.24, 0.11),
        "poly_budget": 340,
        "default_mat": MAT_ASTRAL_ORE,
        "sockets": ["Socket_Grip_Main"],
        "fn": _sculpt_fluid_flask,
    },
    {
        "asset_id": "item_fluid_bucket",
        "dims": (0.28, 0.30, 0.28),
        "poly_budget": 420,
        "default_mat": MAT_OAK_WOOD,
        "sockets": ["Socket_Grip_Main"],
        "fn": _sculpt_fluid_bucket,
    },
    {
        "asset_id": "item_fluid_cauldron",
        "dims": (0.36, 0.30, 0.36),
        "poly_budget": 460,
        "default_mat": MAT_BRONZE,
        "sockets": ["Socket_Grip_Main"],
        "fn": _sculpt_fluid_cauldron,
    },
    {
        "asset_id": "item_flint_stone",
        "dims": (0.15, 0.09, 0.20),
        "poly_budget": 260,
        "default_mat": MAT_BASALT,
        "sockets": ["Socket_Grip_Main"],
        "fn": _sculpt_flint_stone,
    },
    {
        "asset_id": "item_rough_stone",
        "dims": (0.24, 0.16, 0.22),
        "poly_budget": 280,
        "default_mat": MAT_STONE,
        "sockets": ["Socket_Grip_Main"],
        "fn": _sculpt_rough_stone,
    },
    {
        "asset_id": "item_ore_chunk",
        "dims": (0.25, 0.18, 0.23),
        "poly_budget": 420,
        "default_mat": MAT_COPPER_ORE,
        "sockets": ["Socket_Grip_Main"],
        "fn": _sculpt_ore_chunk,
    },
    {
        "asset_id": "item_quartz_cluster",
        "dims": (0.20, 0.24, 0.20),
        "poly_budget": 360,
        "default_mat": MAT_QUARTZ_GEM,
        "sockets": ["Socket_Grip_Main"],
        "fn": _sculpt_quartz_cluster,
    },
    {
        "asset_id": "item_metal_ingot",
        "dims": (0.11, 0.06, 0.26),
        "poly_budget": 220,
        "default_mat": MAT_STEEL,
        "sockets": ["Socket_Grip_Main"],
        "fn": _sculpt_metal_ingot,
    },
    {
        "asset_id": "item_natural_stick",
        "dims": (0.14, 0.08, 0.52),
        "poly_budget": 260,
        "default_mat": MAT_OAK_WOOD,
        "sockets": ["Socket_Grip_Main"],
        "fn": _sculpt_natural_stick,
    },
    {
        "asset_id": "item_split_firewood",
        "dims": (0.18, 0.14, 0.44),
        "poly_budget": 260,
        "default_mat": MAT_OAK_WOOD,
        "sockets": ["Socket_Grip_Main"],
        "fn": _sculpt_split_firewood,
    },
    {
        "asset_id": "item_meat_cut",
        "dims": (0.22, 0.08, 0.17),
        "poly_budget": 320,
        "default_mat": MAT_RUBY_GEM,
        "sockets": ["Socket_Grip_Main"],
        "fn": _sculpt_meat_cut,
    },
    {
        "asset_id": "item_wild_berries",
        "dims": (0.15, 0.11, 0.15),
        "poly_budget": 460,
        "default_mat": MAT_RED_DIAMOND,
        "sockets": ["Socket_Grip_Main"],
        "fn": _sculpt_wild_berries,
    },
    {
        "asset_id": "item_potion_bottle",
        "dims": (0.12, 0.20, 0.12),
        "poly_budget": 320,
        "default_mat": MAT_RUBY_GEM,
        "sockets": ["Socket_Grip_Main"],
        "fn": _sculpt_potion_bottle,
    },
    {
        "asset_id": "item_medical_bandage",
        "dims": (0.13, 0.09, 0.13),
        "poly_budget": 260,
        "default_mat": MAT_TACTICAL_CLOTH,
        "sockets": ["Socket_Grip_Main"],
        "fn": _sculpt_medical_bandage,
    },
    {
        "asset_id": "item_leather_bellows",
        "dims": (0.20, 0.12, 0.36),
        "poly_budget": 360,
        "default_mat": MAT_LEATHER,
        "sockets": ["Socket_Grip_Main"],
        "fn": _sculpt_leather_bellows,
    },
    {
        "asset_id": "item_clay_crucible",
        "dims": (0.22, 0.20, 0.22),
        "poly_budget": 320,
        "default_mat": MAT_CLAY,
        "sockets": ["Socket_Grip_Main"],
        "fn": _sculpt_clay_crucible,
    },
    {
        "asset_id": "item_martial_scroll",
        "dims": (0.14, 0.42, 0.14),
        "poly_budget": 380,
        "default_mat": MAT_SAND,
        "sockets": ["Socket_Grip_Main"],
        "fn": _sculpt_martial_scroll,
    },
]


def _write_glb_binary_color32(
    out_glb_path: Path,
    mesh_name: str,
    positions: list[tuple[float, float, float]],
    normals: list[tuple[float, float, float]],
    colors: list[tuple[int, int, int, int]],
    indices: list[int],
    sockets: dict[str, tuple[float, float, float]],
) -> None:
    out_glb_path.parent.mkdir(parents=True, exist_ok=True)
    v_count = len(positions)
    i_count = len(indices)

    pos_bytes = bytearray()
    nrm_bytes = bytearray()
    col_bytes = bytearray()
    min_x = min_y = min_z = float("inf")
    max_x = max_y = max_z = float("-inf")

    for i in range(v_count):
        px, py, pz = positions[i]
        nx, ny, nz = normals[i]
        cr, cg, cb, ca = colors[i]
        min_x, min_y, min_z = min(min_x, px), min(min_y, py), min(min_z, pz)
        max_x, max_y, max_z = max(max_x, px), max(max_y, py), max(max_z, pz)
        pos_bytes.extend(struct.pack("<fff", px, py, pz))
        nrm_bytes.extend(struct.pack("<fff", nx, ny, nz))
        col_bytes.extend(struct.pack("<BBBB", cr, cg, cb, ca))

    use_u32 = v_count > 65535
    idx_bytes = bytearray()
    for idx in indices:
        idx_bytes.extend(struct.pack("<I" if use_u32 else "<H", idx))
    while len(idx_bytes) % 4 != 0:
        idx_bytes.append(0)

    bin_blob = bytearray()
    pos_off = len(bin_blob)
    bin_blob.extend(pos_bytes)
    nrm_off = len(bin_blob)
    bin_blob.extend(nrm_bytes)
    col_off = len(bin_blob)
    bin_blob.extend(col_bytes)
    idx_off = len(bin_blob)
    bin_blob.extend(idx_bytes)

    nodes: list[dict[str, Any]] = [{"name": mesh_name, "mesh": 0}]
    child_indices: list[int] = []
    for sock_name, (sx, sy, sz) in sockets.items():
        child_indices.append(len(nodes))
        nodes.append({"name": sock_name, "translation": [float(sx), float(sy), float(sz)]})
    if child_indices:
        nodes[0]["children"] = child_indices

    gltf_dict: dict[str, Any] = {
        "asset": {"version": "2.0", "generator": "Stratocraft-Blender42-PT07-ItemFactory"},
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": nodes,
        "meshes": [
            {
                "name": mesh_name,
                "primitives": [
                    {
                        "attributes": {"POSITION": 0, "NORMAL": 1, "COLOR_0": 2},
                        "indices": 3,
                        "mode": 4,
                    }
                ],
            }
        ],
        "buffers": [{"byteLength": len(bin_blob)}],
        "bufferViews": [
            {"buffer": 0, "byteOffset": pos_off, "byteLength": len(pos_bytes), "target": 34962},
            {"buffer": 0, "byteOffset": nrm_off, "byteLength": len(nrm_bytes), "target": 34962},
            {"buffer": 0, "byteOffset": col_off, "byteLength": len(col_bytes), "target": 34962},
            {"buffer": 0, "byteOffset": idx_off, "byteLength": len(idx_bytes), "target": 34963},
        ],
        "accessors": [
            {
                "bufferView": 0,
                "componentType": 5126,
                "count": v_count,
                "type": "VEC3",
                "min": [min_x, min_y, min_z],
                "max": [max_x, max_y, max_z],
            },
            {"bufferView": 1, "componentType": 5126, "count": v_count, "type": "VEC3"},
            {
                "bufferView": 2,
                "componentType": 5121,
                "normalized": True,
                "count": v_count,
                "type": "VEC4",
            },
            {
                "bufferView": 3,
                "componentType": 5125 if use_u32 else 5123,
                "count": i_count,
                "type": "SCALAR",
            },
        ],
    }

    json_bytes = bytearray(json.dumps(gltf_dict, separators=(",", ":")).encode("utf-8"))
    while len(json_bytes) % 4 != 0:
        json_bytes.append(0x20)

    total_len = 12 + 8 + len(json_bytes) + 8 + len(bin_blob)
    with out_glb_path.open("wb") as f:
        f.write(struct.pack("<III", 0x46546C67, 2, total_len))
        f.write(struct.pack("<II", len(json_bytes), 0x4E4F534A))
        f.write(json_bytes)
        f.write(struct.pack("<II", len(bin_blob), 0x004E4942))
        f.write(bin_blob)


def build_single_item_glb(spec: dict[str, Any]) -> Path:
    _reset_scene()
    asset_id = str(spec["asset_id"])
    tx, ty, tz = (float(v) for v in spec["dims"])
    poly_budget = int(spec["poly_budget"])
    default_mat = int(spec["default_mat"])
    req_sockets = [str(s) for s in spec.get("sockets", [])]
    sculpt_fn = spec["fn"]

    bm = bmesh.new()
    layer = _new_rgba_layer(bm)
    sculpt_fn(bm, layer)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])

    mesh_data = bpy.data.meshes.new(asset_id)
    bm.to_mesh(mesh_data)
    bm.free()

    obj = bpy.data.objects.new(asset_id, mesh_data)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj

    if len(mesh_data.polygons) > poly_budget:
        ratio = max(0.12, (poly_budget - 4) / float(len(mesh_data.polygons)))
        dec = obj.modifiers.new(name="SC_ItemDecimate", type="DECIMATE")
        dec.decimate_type = "COLLAPSE"
        dec.ratio = ratio
        depsgraph = bpy.context.evaluated_depsgraph_get()
        eval_obj = obj.evaluated_get(depsgraph)
        eval_mesh = bpy.data.meshes.new_from_object(eval_obj)
    else:
        eval_mesh = mesh_data

    bm_out = bmesh.new()
    bm_out.from_mesh(eval_mesh)
    bmesh.ops.triangulate(bm_out, faces=bm_out.faces[:])
    bm_out.verts.ensure_lookup_table()
    bm_out.faces.ensure_lookup_table()

    xs = [v.co.x for v in bm_out.verts]
    ys = [v.co.y for v in bm_out.verts]
    zs = [v.co.z for v in bm_out.verts]
    min_x, max_x = min(xs), max(xs)
    min_y, max_y = min(ys), max(ys)
    min_z, max_z = min(zs), max(zs)

    cur_dx = max(max_x - min_x, 1e-6)
    cur_dy = max(max_y - min_y, 1e-6)
    cur_dz = max(max_z - min_z, 1e-6)
    mid_x = 0.5 * (min_x + max_x)
    mid_z = 0.5 * (min_z + max_z)

    sx, sy, sz = tx / cur_dx, ty / cur_dy, tz / cur_dz
    for v in bm_out.verts:
        v.co.x = (v.co.x - mid_x) * sx
        v.co.y = (v.co.y - min_y) * sy
        v.co.z = (v.co.z - mid_z) * sz

    bm_out.normal_update()
    out_layer = bm_out.faces.layers.int.get("sc_rgba")

    positions: list[tuple[float, float, float]] = []
    normals: list[tuple[float, float, float]] = []
    colors: list[tuple[int, int, int, int]] = []
    indices: list[int] = []

    faces_list = bm_out.faces[:]
    if len(faces_list) > poly_budget:
        faces_list = faces_list[:poly_budget]

    for f_idx, f in enumerate(faces_list):
        if len(f.verts) != 3:
            continue
        p0 = (float(f.verts[0].co.x), float(f.verts[0].co.y), float(f.verts[0].co.z))
        p1 = (float(f.verts[1].co.x), float(f.verts[1].co.y), float(f.verts[1].co.z))
        p2 = (float(f.verts[2].co.x), float(f.verts[2].co.y), float(f.verts[2].co.z))
        fn = f.normal
        n_tuple = (float(fn.x), float(fn.y), float(fn.z))

        packed = f[out_layer] if out_layer is not None else 0
        if packed != 0:
            r, g, b, a = _unpack_rgba(packed)
        else:
            r, g, b, a = default_mat, 0, 195, 242
        if r == 0:
            r = default_mat

        avg_y01 = max(0.0, min(1.0, (p0[1] + p1[1] + p2[1]) / max(3.0 * ty, 1e-4)))
        facet_jitter = ((f_idx * 43) % 27) - 13
        b_final = max(20, min(255, b + facet_jitter))
        ao_height = int(182 + avg_y01 * 64 + fn.y * 12)
        a_final = max(125, min(255, min(a, ao_height)))
        col_tuple = (r, g, b_final, a_final)

        base_i = len(positions)
        positions.extend((p0, p1, p2))
        normals.extend((n_tuple, n_tuple, n_tuple))
        colors.extend((col_tuple, col_tuple, col_tuple))
        indices.extend((base_i, base_i + 1, base_i + 2))

    bm_out.free()

    sockets: dict[str, tuple[float, float, float]] = {}
    for s in req_sockets:
        sl = s.lower()
        if "grip_main" in sl:
            sockets[s] = (0.0, ty * 0.45, 0.0)
        elif "grip_offhand" in sl:
            sockets[s] = (0.0, ty * 0.22, 0.0)
        elif "voxelblade" in sl:
            sockets[s] = (0.0, ty * 0.92, 0.0)
        else:
            sockets[s] = (0.0, ty * 0.50, 0.0)

    out_glb = OUT_ITEMS_DIR / f"{asset_id}.glb"
    _write_glb_binary_color32(out_glb, asset_id, positions, normals, colors, indices, sockets)

    copy_wep = spec.get("also_copy_to_weapons_mod")
    if copy_wep:
        OUT_WEAPONS_MOD_DIR.mkdir(parents=True, exist_ok=True)
        shutil.copy2(out_glb, OUT_WEAPONS_MOD_DIR / str(copy_wep))

    return out_glb


def main() -> int:
    OUT_ITEMS_DIR.mkdir(parents=True, exist_ok=True)
    for spec in ITEM_CATALOG_SPECS:
        out_path = build_single_item_glb(spec)
        size_kb = out_path.stat().st_size / 1024.0
        print(f"[ItemCatalogGlbBuilder] Built {spec['asset_id']} -> {out_path.name} ({size_kb:.1f} KB)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
