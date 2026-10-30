"""Headless Blender 4.2 LTS (bpy + bmesh) Heykelsi Low-Poly 3B Model Ureticisi.

Komut satiri kullanimi (toplu JSON manifestosu ile tek seferde hizli uretim):
    blender.exe --background --factory-startup --python bpy_sculpt_worker.py -- --batch <jobs.json>

Blender 4.2 glTF exporter BYTE_COLOR niteligini sRGB UNSIGNED_SHORT olarak bozdugu icin,
bu betik geometrik modellemeyi gercek Blender bmesh + Modifier (Decimate / Triangulate) ile yapar,
ardindan tam Color32 (R=MaterialID, G=Temperature, B=Sharpness, A=CavityAO) UNSIGNED_BYTE
nitelikli GLB 2.0 dosyasini dogrudan ikili (binary) formatta hatasiz yazar.
"""

from __future__ import annotations

import json
import math
import struct
import sys
from pathlib import Path
from typing import Any

import bmesh  # type: ignore[import-not-found]
import bpy  # type: ignore[import-not-found]
from mathutils import Euler, Matrix, Vector  # type: ignore[import-not-found]


def _reset_blender_scene() -> None:
    """Fabrika sahnesini sifirlar."""
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for mesh in list(bpy.data.meshes):
        bpy.data.meshes.remove(mesh, do_unlink=True)


def _new_part_layer(bm: bmesh.types.BMesh):
    layer = bm.faces.layers.int.get("sc_rgba")
    if layer is None:
        layer = bm.faces.layers.int.new("sc_rgba")
    return layer


def _pack_rgba_int(r: int, g: int, b: int, a: int) -> int:
    return ((r & 0xFF) << 24) | ((g & 0xFF) << 16) | ((b & 0xFF) << 8) | (a & 0xFF)


def _unpack_rgba_int(packed: int) -> tuple[int, int, int, int]:
    return (
        (packed >> 24) & 0xFF,
        (packed >> 16) & 0xFF,
        (packed >> 8) & 0xFF,
        packed & 0xFF,
    )


def _add_lofted_tube(
    bm: bmesh.types.BMesh,
    rgba_layer: Any,
    stations: list[tuple[tuple[float, float, float], tuple[float, float], float]],
    sides: int,
    rgba: tuple[int, int, int, int],
    cap_ends: bool = True,
    twist_rad: float = 0.0,
) -> None:
    """Cok istasyonlu eliptik kesitli anatomik/mimari parca orer (stations: (center, (rx, rz), pitch))."""
    if len(stations) < 2 or sides < 3:
        return

    packed_col = _pack_rgba_int(*rgba)
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
        r0 = rings[r_idx]
        r1 = rings[r_idx + 1]
        for i in range(sides):
            ni = (i + 1) % sides
            try:
                f = bm.faces.new((r0[i], r0[ni], r1[ni], r1[i]))
                f[rgba_layer] = packed_col
            except ValueError:
                pass

    if cap_ends:
        c_start = stations[0][0]
        v_bot = bm.verts.new(c_start)
        for i in range(sides):
            ni = (i + 1) % sides
            try:
                f = bm.faces.new((v_bot, rings[0][ni], rings[0][i]))
                f[rgba_layer] = packed_col
            except ValueError:
                pass

        c_end = stations[-1][0]
        v_top = bm.verts.new(c_end)
        top_ring = rings[-1]
        for i in range(sides):
            ni = (i + 1) % sides
            try:
                f = bm.faces.new((v_top, top_ring[i], top_ring[ni]))
                f[rgba_layer] = packed_col
            except ValueError:
                pass


def _add_extruded_spine_limb(
    bm: bmesh.types.BMesh,
    rgba_layer: Any,
    spine_pts: list[tuple[tuple[float, float, float], float, float]],
    sides: int,
    rgba: tuple[int, int, int, int],
) -> None:
    """3B uzayda keyfi yone uzanan (bacak, boynuz, kanat kemigi, kuyruk) bukulmus uzuv orer."""
    if len(spine_pts) < 2:
        return
    packed_col = _pack_rgba_int(*rgba)
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
                f[rgba_layer] = packed_col
            except ValueError:
                pass

    v_s = bm.verts.new(spine_pts[0][0])
    for i in range(sides):
        ni = (i + 1) % sides
        try:
            f = bm.faces.new((v_s, rings[0][ni], rings[0][i]))
            f[rgba_layer] = packed_col
        except ValueError:
            pass

    v_e = bm.verts.new(spine_pts[-1][0])
    for i in range(sides):
        ni = (i + 1) % sides
        try:
            f = bm.faces.new((v_e, rings[-1][i], rings[-1][ni]))
            f[rgba_layer] = packed_col
        except ValueError:
            pass


def _add_chamfered_box(
    bm: bmesh.types.BMesh,
    rgba_layer: Any,
    center: tuple[float, float, float],
    size: tuple[float, float, float],
    rgba: tuple[int, int, int, int],
    bevel_amt: float = 0.08,
    rot_euler: tuple[float, float, float] = (0.0, 0.0, 0.0),
) -> None:
    """bmesh kup olusturup Bevel (chamfer) uygular -> DRG tarzi sert pahli makine/zirh blogu."""
    packed_col = _pack_rgba_int(*rgba)
    ret = bmesh.ops.create_cube(bm, size=1.0)
    verts = ret["verts"]

    sx, sy, sz = size
    for v in verts:
        v.co.x *= sx
        v.co.y *= sy
        v.co.z *= sz

    min_dim = min(sx, sy, sz)
    if bevel_amt > 0.001 and min_dim > 0.005:
        geom_edges = list({e for v in verts for e in v.link_edges})
        geom = list(verts) + geom_edges
        bev = bmesh.ops.bevel(
            bm,
            geom=geom,
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
    visited_faces = set()
    for v in all_v:
        v.co = (rot_m @ v.co) + c_vec
        for f in v.link_faces:
            visited_faces.add(f)

    for f in visited_faces:
        if f[rgba_layer] == 0:
            f[rgba_layer] = packed_col


# ==============================================================================
# Kategori Bazli Heykelsi Low-Poly (DRG / V Rising Standardi) Prosedurel Modeller
# ==============================================================================
def _sculpt_calibration_anvil(bm: bmesh.types.BMesh, layer: Any) -> None:
    # Sekizgen tas kaide + bel oyuntulu demir govde + sivri konik boynuzlu celik ors yuzeyi
    _add_lofted_tube(
        bm, layer,
        [((0.0, 0.0, 0.0), (0.38, 0.24), 0.0), ((0.0, 0.22, 0.0), (0.32, 0.20), 0.0)],
        sides=8, rgba=(7, 0, 140, 225),
    )
    _add_lofted_tube(
        bm, layer,
        [
            ((0.0, 0.22, 0.0), (0.26, 0.16), 0.0),
            ((0.0, 0.44, 0.0), (0.16, 0.11), 0.0),
            ((0.0, 0.64, 0.0), (0.28, 0.17), 0.0),
        ],
        sides=8, rgba=(32, 0, 185, 235),
    )
    _add_chamfered_box(bm, layer, (0.0, 0.74, 0.0), (0.50, 0.20, 0.26), (51, 0, 230, 250), bevel_amt=0.14)
    # Konik Ors Boynuzu (+X) ve Arka Topuk (-X)
    _add_extruded_spine_limb(
        bm, layer,
        [((0.22, 0.75, 0.0), 0.11, 0.09), ((0.34, 0.76, 0.0), 0.06, 0.05), ((0.42, 0.77, 0.0), 0.015, 0.015)],
        sides=8, rgba=(51, 0, 245, 252),
    )
    _add_chamfered_box(bm, layer, (-0.30, 0.75, 0.0), (0.18, 0.13, 0.20), (51, 0, 220, 245), bevel_amt=0.12)


def _sculpt_workshop_anvil_station(bm: bmesh.types.BMesh, layer: Any) -> None:
    # 16x32x4 Demirci Istasyonu: Tas ocak + Kor komur yatagi (T^4) + Cift boynuzlu ors + 4 Katalizor sutunu
    _add_lofted_tube(
        bm, layer,
        [((0.0, 0.0, 0.0), (0.56, 0.38), 0.0), ((0.0, 0.16, 0.0), (0.52, 0.35), 0.0)],
        sides=8, rgba=(7, 0, 130, 220),
    )
    # Ana Ors kutugu (Ironwood) ve Cift Boynuzlu Sam Celigi Ors
    _add_lofted_tube(
        bm, layer,
        [((0.0, 0.16, 0.04), (0.26, 0.22), 0.0), ((0.0, 0.56, 0.04), (0.23, 0.19), 0.0)],
        sides=10, rgba=(71, 0, 155, 235),
    )
    _add_chamfered_box(bm, layer, (0.0, 0.72, 0.04), (0.52, 0.24, 0.26), (60, 18, 235, 250), bevel_amt=0.14)
    _add_extruded_spine_limb(
        bm, layer,
        [((0.24, 0.74, 0.04), 0.11, 0.09), ((0.46, 0.76, 0.04), 0.02, 0.02)],
        sides=8, rgba=(60, 25, 248, 252),
    )
    _add_extruded_spine_limb(
        bm, layer,
        [((-0.24, 0.74, 0.04), 0.10, 0.08), ((-0.44, 0.75, 0.04), 0.03, 0.025)],
        sides=6, rgba=(60, 20, 240, 250),
    )
    # Arka Kor Ocak Haznesi (G=225 yuksek sicaklik -> UberShader T^4 Blackbody kor pariltisi!)
    _add_chamfered_box(bm, layer, (0.0, 0.42, -0.24), (0.64, 0.52, 0.24), (1, 0, 145, 230), bevel_amt=0.10)
    _add_lofted_tube(
        bm, layer,
        [((0.0, 0.66, -0.24), (0.24, 0.09), 0.0), ((0.0, 0.76, -0.24), (0.16, 0.06), 0.0)],
        sides=7, rgba=(13, 225, 210, 255),
    )
    # Ors uzerinde dovulen kizgin celik kulce
    _add_chamfered_box(bm, layer, (0.02, 0.87, 0.04), (0.22, 0.06, 0.07), (51, 195, 230, 255), bevel_amt=0.18)
    # 4 Kardinal Harmonizer Katalizor Kaidesi
    for kx, kz, mat in ((0.48, 0.0, 35), (-0.48, 0.0, 37), (0.0, 0.32, 38), (0.0, -0.34, 41)):
        _add_lofted_tube(
            bm, layer,
            [
                ((kx, 0.14, kz), (0.08, 0.08), 0.0),
                ((kx, 0.86, kz), (0.055, 0.055), 0.0),
                ((kx, 1.04, kz), (0.012, 0.012), 0.0),
            ],
            sides=6, rgba=(mat, 45, 235, 250),
        )


def _sculpt_clan_crystal(bm: bmesh.types.BMesh, layer: Any) -> None:
    # Sekizgen bazalt sunak + 4 pencesi + Havada suzulen cok fasetli Astral Rezonans Obeliski + 4 Uydu Kristal
    _add_lofted_tube(
        bm, layer,
        [
            ((0.0, 0.0, 0.0), (0.78, 0.78), 0.0),
            ((0.0, 0.24, 0.0), (0.68, 0.68), 0.0),
            ((0.0, 0.44, 0.0), (0.50, 0.50), 0.0),
        ],
        sides=8, rgba=(6, 0, 180, 230),
    )
    # Altin runik cember
    _add_lofted_tube(
        bm, layer,
        [((0.0, 0.44, 0.0), (0.52, 0.52), 0.0), ((0.0, 0.52, 0.0), (0.44, 0.44), 0.0)],
        sides=8, rgba=(35, 35, 240, 250),
    )
    # Merkez Cift Uclu Fasetli Ana Kristal Monolit (y = 0.62 .. 2.80)
    _add_lofted_tube(
        bm, layer,
        [
            ((0.0, 0.60, 0.0), (0.03, 0.03), 0.0),
            ((0.0, 1.05, 0.0), (0.30, 0.26), 0.0),
            ((0.0, 2.15, 0.0), (0.34, 0.28), 0.0),
            ((0.0, 2.80, 0.0), (0.015, 0.015), 0.0),
        ],
        sides=7, rgba=(41, 65, 248, 255), twist_rad=0.28,
    )
    # 4 dis uydu kristal pence/salkim
    for i in range(4):
        ang = i * (math.pi * 0.5) + (math.pi * 0.25)
        ca, sa = math.cos(ang), math.sin(ang)
        _add_extruded_spine_limb(
            bm, layer,
            [
                ((ca * 0.46, 0.35, sa * 0.46), 0.13, 0.11),
                ((ca * 0.64, 1.15, sa * 0.64), 0.11, 0.09),
                ((ca * 0.54, 1.88, sa * 0.54), 0.015, 0.015),
            ],
            sides=6, rgba=(37, 45, 242, 250),
        )


def _sculpt_weapon_hilt(bm: bmesh.types.BMesh, layer: Any, asset_id: str) -> None:
    is_polearm = "polearm" in asset_id or "shaft" in asset_id
    if is_polearm:
        # 1.65m Disbudak Mizrak/Halberd Sapi + Bronz/Celik bogum halkalari + Ust namlu bilezigi
        _add_lofted_tube(
            bm, layer,
            [
                ((0.0, 0.0, 0.0), (0.024, 0.018), 0.0),
                ((0.0, 0.09, 0.0), (0.016, 0.016), 0.0),
                ((0.0, 0.85, 0.0), (0.019, 0.019), 0.0),
                ((0.0, 1.48, 0.0), (0.022, 0.020), 0.0),
                ((0.0, 1.65, 0.0), (0.032, 0.024), 0.0),
            ],
            sides=8, rgba=(70, 0, 165, 240),
        )
        # Alt mahmuz ve ust kanatli balcik
        _add_lofted_tube(
            bm, layer,
            [((0.0, 0.0, 0.0), (0.006, 0.006), 0.0), ((0.0, 0.11, 0.0), (0.026, 0.022), 0.0)],
            sides=6, rgba=(51, 0, 235, 250),
        )
        _add_chamfered_box(
            bm, layer, (0.0, 1.54, 0.0), (0.16, 0.06, 0.048), (50, 0, 238, 250), bevel_amt=0.18
        )
    else:
        # Uzun Kilic Kabzasi: Elmas kesitli Altin Topuz + Burgulu Demiragaci Sap + Kanatli Balcik
        _add_lofted_tube(
            bm, layer,
            [
                ((0.0, 0.0, 0.0), (0.008, 0.006), 0.0),
                ((0.0, 0.032, 0.0), (0.028, 0.020), 0.0),
                ((0.0, 0.058, 0.0), (0.016, 0.013), 0.0),
            ],
            sides=6, rgba=(35, 0, 245, 252),
        )
        _add_lofted_tube(
            bm, layer,
            [
                ((0.0, 0.058, 0.0), (0.017, 0.012), 0.0),
                ((0.0, 0.155, 0.0), (0.021, 0.014), 0.0),
                ((0.0, 0.250, 0.0), (0.018, 0.013), 0.0),
            ],
            sides=8, rgba=(71, 0, 175, 242), twist_rad=0.65,
        )
        # Yukari kavisli sovalye balcigi (Crossguard) + Merkez Kalkan (Ecusson)
        _add_extruded_spine_limb(
            bm, layer,
            [
                ((-0.12, 0.278, 0.0), 0.010, 0.009),
                ((-0.05, 0.265, 0.0), 0.016, 0.014),
                ((0.0, 0.262, 0.0), 0.024, 0.020),
                ((0.05, 0.265, 0.0), 0.016, 0.014),
                ((0.12, 0.278, 0.0), 0.010, 0.009),
            ],
            sides=6, rgba=(60, 0, 242, 252),
        )
        _add_lofted_tube(
            bm, layer,
            [((0.0, 0.255, 0.0), (0.026, 0.022), 0.0), ((0.0, 0.320, 0.0), (0.020, 0.012), 0.0)],
            sides=6, rgba=(50, 0, 240, 250),
        )


def _sculpt_full_armor_vanguard(bm: bmesh.types.BMesh, layer: Any) -> None:
    # 1.76m Tam Boy Sovalye Zirh Takimi (Sallet Migfer, Gogus Zirhi, Omuzluklar, Eldivenler, Greaves)
    # 1. Migfer (Sallet + Altin Sorguc + Vizor yarigi)
    _add_lofted_tube(
        bm, layer,
        [
            ((0.0, 1.48, 0.0), (0.105, 0.115), 0.0),
            ((0.0, 1.58, 0.02), (0.125, 0.145), 0.0),
            ((0.0, 1.69, -0.01), (0.105, 0.125), 0.0),
            ((0.0, 1.76, -0.02), (0.030, 0.085), 0.0),
        ],
        sides=8, rgba=(51, 0, 238, 252),
    )
    # 2. Gogus Plakasi (Cuirass) + Zincir Etek (Hauberk)
    _add_lofted_tube(
        bm, layer,
        [
            ((0.0, 0.88, 0.0), (0.175, 0.125), 0.0),
            ((0.0, 1.04, 0.0), (0.155, 0.115), 0.0),
            ((0.0, 1.26, 0.02), (0.215, 0.155), 0.0),
            ((0.0, 1.45, 0.0), (0.165, 0.120), 0.0),
        ],
        sides=8, rgba=(51, 0, 232, 248),
    )
    _add_lofted_tube(
        bm, layer,
        [((0.0, 0.74, 0.0), (0.185, 0.132), 0.0), ((0.0, 0.90, 0.0), (0.170, 0.122), 0.0)],
        sides=8, rgba=(32, 0, 185, 228),
    )
    # Altin Gogus Armasi ve Kemer Tokasi
    _add_chamfered_box(bm, layer, (0.0, 1.25, 0.15), (0.12, 0.16, 0.04), (50, 0, 245, 252), bevel_amt=0.22)
    # 3. Simetrik Katmanli Omuzluklar (Pauldrons), Kolcaklar, Eldivenler ve Bacak Greaves/Sabaton
    for sign in (-1.0, 1.0):
        # Pauldron
        _add_lofted_tube(
            bm, layer,
            [
                ((sign * 0.24, 1.32, 0.0), (0.095, 0.115), 0.0),
                ((sign * 0.27, 1.41, 0.0), (0.085, 0.105), 0.0),
                ((sign * 0.22, 1.48, 0.0), (0.045, 0.065), 0.0),
            ],
            sides=7, rgba=(51, 0, 240, 250),
        )
        # Kol + Vambrace + Gauntlet
        _add_extruded_spine_limb(
            bm, layer,
            [
                ((sign * 0.26, 1.32, 0.0), 0.062, 0.062),
                ((sign * 0.28, 1.08, 0.03), 0.056, 0.056),
                ((sign * 0.27, 0.84, 0.07), 0.052, 0.048),
                ((sign * 0.26, 0.72, 0.09), 0.042, 0.032),
            ],
            sides=7, rgba=(32, 0, 205, 240),
        )
        # Uyluk (Cuisse) + Dizlik (Poleyn) + Incik Zirhi (Greave) + Sabaton Cizme
        _add_extruded_spine_limb(
            bm, layer,
            [
                ((sign * 0.11, 0.78, 0.0), 0.082, 0.085),
                ((sign * 0.115, 0.44, 0.02), 0.072, 0.076),
                ((sign * 0.11, 0.10, 0.0), 0.060, 0.065),
                ((sign * 0.11, 0.03, 0.07), 0.058, 0.045),
            ],
            sides=7, rgba=(51, 0, 228, 245),
        )


def _sculpt_individual_armor_slot(bm: bmesh.types.BMesh, layer: Any, asset_id: str) -> None:
    # 15 parcadan her birine (Head/Torso/Arms/Hands/Legs x Layer 1/2/3) OZEL anatomik siluet ve malzeme!
    if "layer1" in asset_id:
        mat, sharp = 75, 130  # Ic Deri / Dolgulu Kumas (Gambeson)
        sides = 8
    elif "layer2" in asset_id:
        mat, sharp = 32, 185  # Orta Perçinli Zincir Zirh (Mail)
        sides = 9
    else:
        mat, sharp = 51, 242  # Dis Dovme Celik Plaka (Plate)
        sides = 7

    rgba = (mat, 0, sharp, 245)
    trim_rgba = (50 if mat == 51 else 74, 0, min(255, sharp + 15), 250)

    if "_head_" in asset_id:
        # Anatomik Migfer / Baslik + Yanaklik + Sorguc
        _add_lofted_tube(
            bm, layer,
            [
                ((0.0, 0.0, 0.0), (0.12, 0.13), 0.0),
                ((0.0, 0.11, 0.01), (0.135, 0.155), 0.0),
                ((0.0, 0.22, -0.01), (0.115, 0.135), 0.0),
                ((0.0, 0.28, -0.02), (0.03, 0.08), 0.0),
            ],
            sides=sides, rgba=rgba,
        )
        if "layer3" in asset_id:
            _add_chamfered_box(bm, layer, (0.0, 0.13, 0.14), (0.16, 0.09, 0.06), trim_rgba, bevel_amt=0.2)
    elif "_torso_" in asset_id:
        # Bel oyuntulu Gambeson / Hauberk / Gogus Zirhi + Omuz askilari
        _add_lofted_tube(
            bm, layer,
            [
                ((0.0, 0.0, 0.0), (0.19, 0.135), 0.0),
                ((0.0, 0.18, 0.0), (0.165, 0.120), 0.0),
                ((0.0, 0.40, 0.02), (0.225, 0.160), 0.0),
                ((0.0, 0.56, 0.0), (0.175, 0.120), 0.0),
            ],
            sides=sides, rgba=rgba,
        )
        _add_chamfered_box(bm, layer, (0.0, 0.36, 0.15), (0.14, 0.18, 0.04), trim_rgba, bevel_amt=0.2)
    elif "_arms_" in asset_id:
        # Cift Kol / Omuzluk & Vambrace Takimi
        for sign in (-1.0, 1.0):
            _add_extruded_spine_limb(
                bm, layer,
                [
                    ((sign * 0.15, 0.48, 0.0), 0.085, 0.095),
                    ((sign * 0.17, 0.26, 0.02), 0.065, 0.068),
                    ((sign * 0.16, 0.02, 0.04), 0.052, 0.054),
                ],
                sides=sides, rgba=rgba,
            )
    elif "_hands_" in asset_id:
        # Cift Zirhli Eldiven (Manset + El Tarak Zirhi + Parmaklar)
        for sign in (-1.0, 1.0):
            _add_lofted_tube(
                bm, layer,
                [
                    ((sign * 0.11, 0.0, 0.0), (0.055, 0.048), 0.0),
                    ((sign * 0.11, 0.12, 0.0), (0.045, 0.038), 0.0),
                    ((sign * 0.11, 0.24, 0.02), (0.052, 0.028), 0.0),
                ],
                sides=sides, rgba=rgba,
            )
    else:
        # Cift Bacak Zirhi (Chausses / Greaves + Diz Kalkanlari)
        for sign in (-1.0, 1.0):
            _add_extruded_spine_limb(
                bm, layer,
                [
                    ((sign * 0.12, 0.72, 0.0), 0.085, 0.088),
                    ((sign * 0.12, 0.38, 0.03), 0.075, 0.082),
                    ((sign * 0.12, 0.06, 0.0), 0.062, 0.065),
                    ((sign * 0.12, 0.02, 0.08), 0.058, 0.042),
                ],
                sides=sides, rgba=rgba,
            )


def _sculpt_creature_ash_stalker(bm: bmesh.types.BMesh, layer: Any) -> None:
    # 1.85m Boyunda Kambur Yirtici Kul Avcisi (Ash Stalker - 0.85x1.85x0.65m):
    # Digitigrade bacaklar, kambur gogus, obsidyen dikenler, penceli kollar, kor gozlu disli bas
    fur_col = (77, 0, 155, 240)
    plate_col = (6, 20, 215, 248)
    claw_col = (76, 0, 235, 252)
    ember_col = (13, 240, 245, 255)

    # Bel -> Kasli Kambur Gogus -> Boyun omurgasi (+Y yukari, hafif +Z one egik)
    _add_extruded_spine_limb(
        bm, layer,
        [
            ((0.0, 0.82, -0.06), 0.15, 0.13),
            ((0.0, 1.06, -0.03), 0.18, 0.15),
            ((0.0, 1.34, 0.04), 0.24, 0.20),
            ((0.0, 1.54, 0.10), 0.16, 0.15),
        ],
        sides=8, rgba=fur_col,
    )
    # One uzanan Yirtici Kafatasi + Cene (+Z ileri)
    _add_extruded_spine_limb(
        bm, layer,
        [
            ((0.0, 1.56, 0.10), 0.13, 0.13),
            ((0.0, 1.68, 0.18), 0.125, 0.12),
            ((0.0, 1.64, 0.29), 0.075, 0.070),
            ((0.0, 1.60, 0.35), 0.042, 0.038),
        ],
        sides=7, rgba=fur_col,
    )
    # Geriye kavisli Boynuz/Kulaklar, Kor Gozler ve Fildisi Disler
    for sign in (-1.0, 1.0):
        _add_extruded_spine_limb(
            bm, layer,
            [((sign * 0.08, 1.72, 0.14), 0.038, 0.032), ((sign * 0.13, 1.85, 0.02), 0.010, 0.010)],
            sides=5, rgba=plate_col,
        )
        _add_chamfered_box(
            bm, layer, (sign * 0.075, 1.69, 0.23), (0.035, 0.028, 0.04), ember_col, bevel_amt=0.2
        )
        _add_extruded_spine_limb(
            bm, layer,
            [((sign * 0.038, 1.59, 0.31), 0.015, 0.015), ((sign * 0.036, 1.51, 0.32), 0.004, 0.004)],
            sides=5, rgba=claw_col,
        )

    # Sirt Obsidyen Zirh Dikenleri + Gogus Kor Cekirdegi (WeakPoint_ChestCore)
    _add_chamfered_box(bm, layer, (0.0, 1.32, 0.21), (0.12, 0.15, 0.06), ember_col, bevel_amt=0.22)
    for y_sp, z_sp, len_sp in ((1.52, -0.02, 0.18), (1.36, -0.10, 0.20), (1.16, -0.12, 0.16)):
        _add_extruded_spine_limb(
            bm, layer,
            [((0.0, y_sp, z_sp), 0.05, 0.06), ((0.0, y_sp + len_sp * 0.7, z_sp - len_sp), 0.010, 0.012)],
            sides=5, rgba=plate_col,
        )

    # Uzun Penceli Yirtici Kollar (UpperArm + ForeArm + Pence)
    for sign in (-1.0, 1.0):
        _add_extruded_spine_limb(
            bm, layer,
            [
                ((sign * 0.24, 1.40, 0.04), 0.095, 0.095),
                ((sign * 0.35, 1.12, 0.08), 0.072, 0.072),
                ((sign * 0.38, 0.82, 0.18), 0.062, 0.058),
                ((sign * 0.36, 0.66, 0.25), 0.055, 0.035),
            ],
            sides=7, rgba=fur_col,
        )
        # Uzun Fildisi El Penceleri
        for c_off in (-0.025, 0.0, 0.025):
            _add_extruded_spine_limb(
                bm, layer,
                [
                    ((sign * 0.36 + c_off, 0.66, 0.25), 0.014, 0.014),
                    ((sign * 0.35 + c_off, 0.54, 0.31), 0.003, 0.003),
                ],
                sides=4, rgba=claw_col,
            )

        # Digitigrade (Ters Eklemli) Guclu Avci Bacaklari
        _add_extruded_spine_limb(
            bm, layer,
            [
                ((sign * 0.16, 0.86, -0.04), 0.105, 0.110),
                ((sign * 0.19, 0.52, 0.10), 0.078, 0.082),
                ((sign * 0.18, 0.24, -0.10), 0.056, 0.060),
                ((sign * 0.18, 0.04, 0.06), 0.068, 0.042),
            ],
            sides=7, rgba=fur_col,
        )

    # Dengeli Kuyruk
    _add_extruded_spine_limb(
        bm, layer,
        [
            ((0.0, 0.82, -0.10), 0.065, 0.065),
            ((0.0, 0.56, -0.24), 0.042, 0.042),
            ((0.0, 0.38, -0.30), 0.016, 0.016),
        ],
        sides=6, rgba=fur_col,
    )


def _sculpt_boss_phoenix(bm: bmesh.types.BMesh, layer: Any) -> None:
    # 3.60m Kanat Aciklikli Anka Kusu (Phoenix): Altin Gaga, Kor Kalp, Katmanli Tuy Kanatlar, Penceler
    crimson = (39, 95, 225, 248)
    gold_plume = (78, 155, 240, 252)
    ember_core = (13, 248, 250, 255)
    beak_gold = (46, 80, 248, 255)

    #Suzulen Kayalik/Kor Sunak Tabani + Penceler (y=0.0..1.4)
    _add_lofted_tube(
        bm, layer,
        [((0.0, 0.0, 0.0), (0.55, 0.48), 0.0), ((0.0, 0.35, 0.0), (0.32, 0.28), 0.0)],
        sides=7, rgba=(6, 90, 200, 235),
    )
    for sign in (-1.0, 1.0):
        _add_extruded_spine_limb(
            bm, layer,
            [
                ((sign * 0.26, 1.65, 0.05), 0.14, 0.15),
                ((sign * 0.30, 0.95, 0.12), 0.09, 0.09),
                ((sign * 0.28, 0.32, 0.18), 0.08, 0.06),
            ],
            sides=6, rgba=beak_gold,
        )

    # Kavisli Kus Govdesi + Parlayan Kor Gogus Cekirdegi + Kancali Gaga + Tac Sorgucu
    _add_extruded_spine_limb(
        bm, layer,
        [
            ((0.0, 1.55, -0.35), 0.34, 0.36),
            ((0.0, 2.25, 0.10), 0.48, 0.52),
            ((0.0, 2.95, 0.42), 0.32, 0.34),
            ((0.0, 3.55, 0.62), 0.22, 0.22),
            ((0.0, 3.62, 0.95), 0.11, 0.10),
        ],
        sides=8, rgba=crimson,
    )
    # Kancali Altin Gaga
    _add_extruded_spine_limb(
        bm, layer,
        [((0.0, 3.60, 0.92), 0.11, 0.10), ((0.0, 3.48, 1.24), 0.02, 0.025)],
        sides=5, rgba=beak_gold,
    )
    # Gogus Kor Cekirdegi
    _add_lofted_tube(
        bm, layer,
        [((0.0, 2.05, 0.48), (0.18, 0.12), 0.0), ((0.0, 2.55, 0.54), (0.22, 0.14), 0.0)],
        sides=7, rgba=ember_core,
    )
    # 3'lu Bas Tac Sorgucu
    for sx, h_off in ((-0.12, 4.05), (0.0, 4.20), (0.12, 4.05)):
        _add_extruded_spine_limb(
            bm, layer,
            [((sx * 0.5, 3.68, 0.58), 0.05, 0.05), ((sx, h_off, 0.22), 0.012, 0.015)],
            sides=5, rgba=ember_core,
        )

    # Acilmis Gorjemli Cift Katmanli Kanatlar + Telek Tuyleri
    for sign in (-1.0, 1.0):
        _add_extruded_spine_limb(
            bm, layer,
            [
                ((sign * 0.35, 2.55, 0.15), 0.22, 0.14),
                ((sign * 1.05, 3.15, -0.05), 0.18, 0.11),
                ((sign * 1.65, 3.55, -0.25), 0.12, 0.08),
                ((sign * 1.80, 3.30, -0.45), 0.04, 0.03),
            ],
            sides=6, rgba=crimson,
        )
        # 4 Uzun Altin-Kor Kanat Telek Tuyu
        for f_idx in range(4):
            t = (f_idx + 1) / 4.0
            wx = sign * (0.65 + t * 1.10)
            wy = 2.65 + t * 0.70
            _add_extruded_spine_limb(
                bm, layer,
                [
                    ((wx, wy, -0.05 - t * 0.2), 0.11, 0.04),
                    ((wx * 0.96, wy - 0.85, -0.55 - t * 0.25), 0.02, 0.015),
                ],
                sides=5, rgba=gold_plume if f_idx % 2 == 0 else ember_core,
            )

    # Yelpaze Kuyruk Tuyleri
    for t_idx in range(-2, 3):
        _add_extruded_spine_limb(
            bm, layer,
            [
                ((t_idx * 0.10, 1.55, -0.35), 0.10, 0.05),
                ((t_idx * 0.26, 0.85, -1.35), 0.025, 0.015),
            ],
            sides=5, rgba=gold_plume if abs(t_idx) == 1 else crimson,
        )


def _sculpt_siege_trebuchet(bm: bmesh.types.BMesh, layer: Any) -> None:
    # 5 Asamali Kusatma Mancinigi: 4 Tekerlek + A-Iskelet + Karsi Agirlik Sandigi + Uzun Atis Kolu
    wood_dark = (71, 0, 155, 238)
    wood_arm = (70, 0, 170, 244)
    iron_band = (51, 0, 230, 248)
    stone_wt = (1, 0, 140, 225)

    # Alt Sasi Kirisleri
    for sign in (-1.0, 1.0):
        _add_chamfered_box(bm, layer, (sign * 0.95, 0.42, 0.0), (0.22, 0.22, 4.00), wood_dark, bevel_amt=0.12)
    for z_cross in (-1.65, 0.0, 1.65):
        _add_chamfered_box(bm, layer, (0.0, 0.42, z_cross), (2.10, 0.20, 0.22), wood_dark, bevel_amt=0.12)

    # 4 Sekizgen Kusatma Tekerlegi (X ekseni boyunca doner)
    for sx in (-1.18, 1.18):
        for sz in (-1.45, 1.45):
            _add_extruded_spine_limb(
                bm, layer,
                [((sx - 0.12, 0.42, sz), 0.42, 0.42), ((sx + 0.12, 0.42, sz), 0.42, 0.42)],
                sides=8, rgba=iron_band,
            )

    # Cift A-Cerceve Ana Tasiyici Dikmeler ve Ust Demir Mil
    for sign in (-1.0, 1.0):
        _add_extruded_spine_limb(
            bm, layer,
            [((sign * 0.92, 0.48, -1.15), 0.12, 0.12), ((sign * 0.62, 2.55, 0.20), 0.10, 0.10)],
            sides=6, rgba=wood_dark,
        )
        _add_extruded_spine_limb(
            bm, layer,
            [((sign * 0.92, 0.48, 1.45), 0.12, 0.12), ((sign * 0.62, 2.55, 0.20), 0.10, 0.10)],
            sides=6, rgba=wood_dark,
        )
    _add_extruded_spine_limb(
        bm, layer,
        [((-0.82, 2.52, 0.20), 0.08, 0.08), ((0.82, 2.52, 0.20), 0.08, 0.08)],
        sides=8, rgba=iron_band,
    )

    #Egik Uzun Atis Kolu + Asili Karsi Agirlik Sandigi
    _add_extruded_spine_limb(
        bm,
        layer,
        [
            ((0.0, 2.20, 1.15), 0.14, 0.14),
            ((0.0, 2.52, 0.20), 0.12, 0.12),
            ((0.0, 3.38, -2.05), 0.055, 0.055),
        ],
        sides=7,
        rgba=wood_arm,
    )
    _add_lofted_tube(
        bm, layer,
        [((0.0, 1.05, 1.10), (0.36, 0.36), 0.0), ((0.0, 1.95, 1.10), (0.46, 0.44), 0.0)],
        sides=6, rgba=stone_wt,
    )


def _write_glb_binary(
    out_glb_path: Path,
    mesh_name: str,
    positions: list[tuple[float, float, float]],
    normals: list[tuple[float, float, float]],
    colors: list[tuple[int, int, int, int]],
    indices: list[int],
    sockets: dict[str, tuple[float, float, float]],
) -> None:
    """Standartlara uyumlu glTF 2.0 Binary (.glb) uretir (COLOR_0 = VEC4 UNSIGNED_BYTE normalized)."""
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
    if use_u32:
        for idx in indices:
            idx_bytes.extend(struct.pack("<I", idx))
    else:
        for idx in indices:
            idx_bytes.extend(struct.pack("<H", idx))
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
        "asset": {"version": "2.0", "generator": "Stratocraft-Blender42-SculptFactory"},
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


def _compute_socket_positions(
    required_sockets: list[str],
    tx: float,
    ty: float,
    tz: float,
) -> dict[str, tuple[float, float, float]]:
    sockets: dict[str, tuple[float, float, float]] = {}
    for s in required_sockets:
        sl = s.lower()
        if "head" in sl:
            sockets[s] = (0.0, ty * 0.86, tz * 0.15)
        elif "chest" in sl or "torso" in sl or "ritualcore" in sl:
            sockets[s] = (0.0, ty * 0.62, 0.0)
        elif "voxelblade" in sl or "anviltop" in sl or "worksurface" in sl:
            sockets[s] = (0.0, ty * 0.96, 0.0)
        elif "grip_main" in sl:
            sockets[s] = (0.0, ty * 0.45, 0.0)
        elif "grip_offhand" in sl:
            sockets[s] = (0.0, ty * 0.22, 0.0)
        elif "catalystnorth" in sl:
            sockets[s] = (0.0, ty * 0.95, tz * 0.42)
        elif "catalystsouth" in sl:
            sockets[s] = (0.0, ty * 0.95, -tz * 0.42)
        elif "catalysteast" in sl:
            sockets[s] = (tx * 0.42, ty * 0.95, 0.0)
        elif "catalystwest" in sl:
            sockets[s] = (-tx * 0.42, ty * 0.95, 0.0)
        elif "l_forearm" in sl or "arml" in sl:
            sockets[s] = (-tx * 0.36, ty * 0.56, 0.0)
        elif "r_forearm" in sl or "armr" in sl:
            sockets[s] = (tx * 0.36, ty * 0.56, 0.0)
        elif "l_calf" in sl or "legl" in sl:
            sockets[s] = (-tx * 0.22, ty * 0.24, 0.0)
        elif "r_calf" in sl or "legr" in sl:
            sockets[s] = (tx * 0.22, ty * 0.24, 0.0)
        elif "muzzle" in sl:
            sockets[s] = (0.0, ty * 0.96, -tz * 0.45)
        elif "emberegg" in sl:
            sockets[s] = (0.0, ty * 0.12, 0.0)
        else:
            sockets[s] = (0.0, ty * 0.50, 0.0)
    return sockets


def generate_sculpted_job(job: dict[str, Any]) -> dict[str, Any]:
    """Tek bir varlik isini Blender 4.2 bmesh + Modifier ile uretir ve GLB olarak yazar."""
    _reset_blender_scene()

    asset_id = str(job["asset_id"])
    category = str(job["category"])
    tx, ty, tz = (float(v) for v in job["target_dimensions_meters"])
    poly_budget = int(job["poly_budget_lod0"])
    default_mat = max(1, int(job["default_material_id"]))
    required_sockets = [str(s) for s in job.get("required_sockets", [])]
    out_glb = Path(str(job["out_glb_path"]))

    bm = bmesh.new()
    layer = _new_part_layer(bm)

    if category == "CalibrationProp":
        _sculpt_calibration_anvil(bm, layer)
    elif category == "WeaponHilt":
        _sculpt_weapon_hilt(bm, layer, asset_id)
    elif category == "Armor15Piece":
        if asset_id.startswith("armor_slot_"):
            _sculpt_individual_armor_slot(bm, layer, asset_id)
        else:
            _sculpt_full_armor_vanguard(bm, layer)
    elif category == "WorkshopDevice":
        _sculpt_workshop_anvil_station(bm, layer)
    elif category == "ClanStructure":
        _sculpt_clan_crystal(bm, layer)
    elif category == "SiegeWeapon":
        _sculpt_siege_trebuchet(bm, layer)
    elif category == "Boss":
        _sculpt_boss_phoenix(bm, layer)
    elif category == "Creature":
        _sculpt_creature_ash_stalker(bm, layer)
    else:
        _sculpt_calibration_anvil(bm, layer)

    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    mesh_data = bpy.data.meshes.new(asset_id)
    bm.to_mesh(mesh_data)
    bm.free()

    obj = bpy.data.objects.new(asset_id, mesh_data)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj

    # Eger ucgen sayisi LOD0 butcesini asiyorsa Blender Decimate Modifier ile siluet koruyarak indir
    if len(mesh_data.polygons) > poly_budget:
        ratio = max(0.08, (poly_budget - 4) / float(len(mesh_data.polygons)))
        dec = obj.modifiers.new(name="SC_BudgetDecimate", type="DECIMATE")
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

    # Exact AABB & BottomCenter_Zero Pivot Hizalamasi (%0.00 sapma garantisi)
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
            r, g, b, a = _unpack_rgba_int(packed)
        else:
            r, g, b, a = default_mat, 0, 180, 240
        if r == 0:
            r = default_mat

        # Low-Poly faset kontrast & yukseklik Cavity AO modulasyonu
        avg_y01 = max(0.0, min(1.0, (p0[1] + p1[1] + p2[1]) / max(3.0 * ty, 1e-4)))
        facet_jitter = ((f_idx * 43) % 31) - 15
        b_final = max(12, min(255, b + facet_jitter))
        ao_height = int(175 + avg_y01 * 70 + fn.y * 12)
        a_final = max(115, min(255, min(a, ao_height)))
        col_tuple = (r, g, b_final, a_final)

        base_i = len(positions)
        positions.extend((p0, p1, p2))
        normals.extend((n_tuple, n_tuple, n_tuple))
        colors.extend((col_tuple, col_tuple, col_tuple))
        indices.extend((base_i, base_i + 1, base_i + 2))

    bm_out.free()
    sockets = _compute_socket_positions(required_sockets, tx, ty, tz)
    _write_glb_binary(out_glb, asset_id, positions, normals, colors, indices, sockets)
    return {"asset_id": asset_id, "triangles": len(indices) // 3, "path": str(out_glb)}


def main() -> None:
    argv = sys.argv
    if "--" not in argv:
        raise SystemExit(
            "Kullanim: blender --background --python bpy_sculpt_worker.py -- --batch <jobs.json>"
        )
    user_args = argv[argv.index("--") + 1 :]
    if len(user_args) >= 2 and user_args[0] == "--batch":
        batch_path = Path(user_args[1])
        payload = json.loads(batch_path.read_text(encoding="utf-8"))
        results = [generate_sculpted_job(item) for item in payload["jobs"]]
        out_report = batch_path.with_suffix(".report.json")
        out_report.write_text(json.dumps({"results": results}, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
