"""
Stratocraft Otonom 3B Fabrika — Ic Mekan, Kapi/Pencere/Merdiven/Somine ve Santiye Ureticisi (Blender 4.2).

Kullanim (0 gomulu doku, UV-free COLOR_0, < 16.0 MB VRAM):
    blender --background --python Tools/AutonomousAssetFactory/generators/interior_and_site_kit_builder.py

Uretilen 10 Ic Mekan & Santiye .glb Parca (Assets/Art/Generated/Structures_Modular/*.glb):
    1. mod_hinged_door_leaf.glb      — Menteseli & ic surgulu mese kapi kanadi (0.98x0.10x1.92m)
    2. mod_double_gate_leaf.glb      — Demir kusakli ve kale surgulu cift kanatli saray kapisi (2.40x2.80m)
    3. mod_shuttered_window_2x2m.glb — Mimari camli ve acilir ahsap kepenkli pencere modulu (2.0x2.0m)
    4. mod_stairs_straight_2m.glb    — Limon kirisli, 8 basamakli ve tirabzanli duz ahsap merdiven (2.0m)
    5. mod_stairs_spiral_stone.glb   — Merkez tas sutunlu 360° (12 kama basamakli) sarmal tas merdiven (3.0m)
    6. mod_fireplace_hearth.glb      — Yanmaz tas tabanli, koz yatakli ve davlumbazli tas/tugla somine (2.0m)
    7. mod_chimney_flue_2m.glb       — Yanmaz yakali ve yagmur sapkali tas/tugla baca safti (0.78x0.78x2.0m)
    8. mod_scaffolding_frame_2m.glb  — Tirmanma merdivenli ve korkuluklu ahsap santiye iskelesi (2.0x2.0x2.0m)
    9. mod_pulley_crane_3m.glb       — 360° doner kollu, cikrikli ve demir kancali santiye vinci (3.0m)
   10. mod_site_material_pile.glb    — Kutuk, kalas, kesme tas ve harc teknesi santiye yigini (2.0x2.0m)
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, "..", "..", ".."))
OUT_MODULAR_DIR = os.path.join(REPO_ROOT, "Assets", "Art", "Generated", "Structures_Modular")

# Ic Mekan Ahsabi, Demir Hirdavat, Mimari Cam, Somine Tasi/Tuglasi ve Santiye Paleti (sRGB -> COLOR_0)
# R kanali asla 0, 3 veya 11 secilmez.
PAL = {
    "oak_plank_warm": (172, 122, 74, 246),
    "oak_plank_mid": (154, 106, 62, 242),
    "oak_plank_dark": (124, 84, 48, 234),
    "ironwood_heavy": (106, 70, 42, 238),
    "peeled_pole": (194, 148, 92, 246),
    "wrought_iron": (62, 66, 74, 240),
    "wrought_iron_dark": (46, 50, 56, 234),
    "brass_ring": (184, 146, 76, 248),
    "arch_glass": (164, 206, 218, 212),
    "lead_came": (82, 86, 94, 238),
    "hearth_stone_warm": (152, 144, 132, 244),
    "hearth_stone_dark": (96, 98, 104, 234),
    "soot_char": (42, 38, 36, 224),
    "ember_glow": (228, 96, 38, 252),
    "fired_brick": (178, 88, 64, 244),
    "fired_brick_dark": (146, 70, 50, 238),
    "lime_mortar": (204, 198, 186, 236),
    "hemp_rope": (188, 162, 112, 242),
    "ashlar_block": (186, 180, 168, 248),
}


def srgb_to_linear_rgba(rgba_u8):
    """Blender 4.2 -> glTF COLOR_0 icin kesin sRGB->Linear donusumu."""

    def _c(v):
        x = max(0.0, min(1.0, v / 255.0))
        return x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4

    r_safe = int(rgba_u8[0])
    if r_safe in (0, 3, 11):
        r_safe += 1
    return (_c(r_safe), _c(rgba_u8[1]), _c(rgba_u8[2]), rgba_u8[3] / 255.0)


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def create_preview_vertex_color_material():
    mat = bpy.data.materials.new(name="M_InteriorSiteVertexColorPreview")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    vcol = nt.nodes.new("ShaderNodeVertexColor")
    vcol.layer_name = "Col"
    bsdf.inputs["Roughness"].default_value = 0.72
    bsdf.inputs["Specular IOR Level"].default_value = 0.24
    nt.links.new(vcol.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


class InteriorSiteMeshBuilder:
    """UV-free COLOR_0 ic mekan ve santiye bmesh insacisi."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.color_layer = self.bm.loops.layers.color.new("Col")

    def paint_faces(self, faces, rgba_u8, z_ao_origin=0.0, z_ao_span=2.0, z_ao_strength=0.10):
        lin = srgb_to_linear_rgba(rgba_u8)
        for f in faces:
            if not f.is_valid:
                continue
            cz = f.calc_center_median().z
            nrm = f.normal
            t_h = max(0.0, min(1.0, (cz - z_ao_origin) / max(0.25, z_ao_span)))
            shade = (1.0 - z_ao_strength * 0.65) + z_ao_strength * t_h
            if nrm.z < -0.45:
                shade *= 0.84
            elif nrm.z > 0.55:
                shade *= 1.05
            ao = max(0.55, min(1.0, lin[3] * (0.88 + 0.12 * t_h)))
            col = (
                max(0.01, min(1.0, lin[0] * shade)),
                max(0.01, min(1.0, lin[1] * shade)),
                max(0.01, min(1.0, lin[2] * shade)),
                ao,
            )
            for loop in f.loops:
                loop[self.color_layer] = col

    def add_box(self, center, half_extents, rgba_u8, rot_euler=(0.0, 0.0, 0.0), bevel_amt=0.006):
        prev_faces = set(self.bm.faces)
        rot_m = Euler(rot_euler, "XYZ").to_matrix().to_4x4()
        scale_m = Matrix.Diagonal(
            (half_extents[0] * 2.0, half_extents[1] * 2.0, half_extents[2] * 2.0, 1.0)
        )
        xform = Matrix.Translation(Vector(center)) @ rot_m @ scale_m
        bmesh.ops.create_cube(self.bm, size=1.0, matrix=xform)
        new_faces = [f for f in self.bm.faces if f not in prev_faces]
        if bevel_amt > 0.0:
            edges = list({e for f in new_faces if f.is_valid for e in f.edges})
            bmesh.ops.bevel(self.bm, geom=edges, offset=bevel_amt, segments=1, affect="EDGES")
            new_faces = [f for f in self.bm.faces if f not in prev_faces and f.is_valid]
        bmesh.ops.recalc_face_normals(self.bm, faces=new_faces)
        self.paint_faces(new_faces, rgba_u8)
        return new_faces

    def finalize_object(self, mat, smooth_shading=False):
        bmesh.ops.triangulate(self.bm, faces=self.bm.faces[:])
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces[:])

        mesh = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(mesh)
        self.bm.free()

        for p in mesh.polygons:
            p.use_smooth = smooth_shading

        obj = bpy.data.objects.new(self.name, mesh)
        obj.location = Vector((0.0, 0.0, 0.0))
        obj.data.materials.append(mat)
        bpy.context.collection.objects.link(obj)
        return obj


# ==============================================================================
# 10 IC MEKAN & SANTIYE PARCASI URETICILERI
# ==============================================================================

def build_mod_hinged_door_leaf(mat):
    """
    0.98m genislik x 1.92m yukseklik x 0.10m kalinlikta dikey mese kalasli, Z-kusakli,
    uzun dovme demir yaprak menteseli ve icten kayar surgulu (Slide Bolt) ev kapi kanadi.
    """
    mb = InteriorSiteMeshBuilder("Mod_Hinged_Door_Leaf")
    # 5 dikey mese kalas (X in [-0.48..+0.48], Z in [0.0..1.92])
    tones = (PAL["oak_plank_warm"], PAL["oak_plank_mid"], PAL["oak_plank_dark"])
    planks = 5
    step_x = 0.96 / planks
    for i in range(planks):
        cx = -0.48 + (i + 0.5) * step_x
        mb.add_box((cx, 0.0, 0.96), (step_x * 0.48, 0.028, 0.96), tones[i % len(tones)], bevel_amt=0.006)

    # Arka/On Z-kusak (ledge & diagonal brace)
    for kz in (0.28, 1.64):
        mb.add_box((0.0, 0.036, kz), (0.46, 0.018, 0.065), PAL["ironwood_heavy"], bevel_amt=0.005)
    mb.add_box(
        (0.0, 0.036, 0.96),
        (0.055, 0.016, 0.72),
        PAL["ironwood_heavy"],
        rot_euler=(0.0, -0.56, 0.0),
        bevel_amt=0.005,
    )

    # Uzun dovme demir yaprak menteseler (sol kenar X=-0.48'den uzanan strap hinges)
    for hz in (0.28, 1.64):
        mb.add_box((-0.14, -0.034, hz), (0.34, 0.008, 0.032), PAL["wrought_iron"], bevel_amt=0.003)
        mb.add_box((-0.485, 0.0, hz), (0.018, 0.036, 0.055), PAL["wrought_iron_dark"], bevel_amt=0.004)

    # Sag kenarda (X=+0.36m) dis halka tokmak + ic kayar demir surgu ([Shift+E] kilidi)
    mb.add_box((0.36, -0.040, 0.96), (0.045, 0.014, 0.075), PAL["wrought_iron_dark"], bevel_amt=0.003)
    mb.add_box((0.36, -0.058, 0.93), (0.038, 0.012, 0.038), PAL["brass_ring"], bevel_amt=0.004)
    mb.add_box((0.35, 0.052, 0.96), (0.13, 0.016, 0.024), PAL["wrought_iron"], bevel_amt=0.003)
    return mb.finalize_object(mat)


def build_mod_double_gate_leaf(mat):
    """
    2.40m toplam genislik (2 x 1.20m kanat) x 2.80m yukseklik x 0.16m kalinlikta demir agaci
    ve dovme demir kusakli, arka agir kale surgulu Cift Kanatli Kale / Saray Kapisi.
    """
    mb = InteriorSiteMeshBuilder("Mod_Double_Gate_Leaf")
    tones = (PAL["ironwood_heavy"], PAL["oak_plank_dark"], PAL["oak_plank_mid"])
    # Sol (-1.20..-0.01) ve Sag (+0.01..+1.20) agır kapi kanatlari
    for side in (-1.0, 1.0):
        for p in range(4):
            cx = side * (0.15 + p * 0.28)
            h_top = 2.78 - (p * 0.08)  # Ustte hafif kemerli kavis
            mb.add_box(
                (cx, 0.0, h_top * 0.5),
                (0.132, 0.052, h_top * 0.5),
                tones[p % len(tones)],
                bevel_amt=0.008,
            )
        # 3 sira percinli agir dovme demir kusak + dis mentese babalari
        for bz in (0.38, 1.38, 2.28):
            mb.add_box(
                (side * 0.60, -0.058, bz),
                (0.58, 0.014, 0.055),
                PAL["wrought_iron"],
                bevel_amt=0.004,
            )
            mb.add_box(
                (side * 1.18, 0.0, bz),
                (0.032, 0.072, 0.085),
                PAL["wrought_iron_dark"],
                bevel_amt=0.005,
            )
            for rx in (0.22, 0.58, 0.94):
                mb.add_box(
                    (side * rx, -0.074, bz),
                    (0.018, 0.010, 0.018),
                    PAL["brass_ring"],
                    bevel_amt=0.003,
                )

    # Orta binili kapama citasi (astragal) + arka agir travers kale kalas surgusu (Z=1.38m)
    mb.add_box((0.0, -0.058, 1.38), (0.045, 0.018, 1.36), PAL["wrought_iron_dark"], bevel_amt=0.005)
    mb.add_box((0.0, 0.085, 1.38), (1.12, 0.048, 0.065), PAL["oak_plank_warm"], bevel_amt=0.008)
    for sx in (-0.75, 0.75):
        mb.add_box((sx, 0.075, 1.38), (0.045, 0.058, 0.095), PAL["wrought_iron_dark"], bevel_amt=0.004)
    return mb.finalize_object(mat)


def build_mod_shuttered_window_2x2m(mat):
    """
    2.0m x 2.0m duvar modulunde tas denizlikli pencere kasasi, kursun kafesli mimari cam panel
    (Architectural Glass) ve iki yana acilmis demir menteseli Ahsap Kepenkler (Wooden Shutters).
    """
    mb = InteriorSiteMeshBuilder("Mod_Shuttered_Window_2x2m")
    # Dis 2x2m karkas + alt/ust kalas dolgular
    mb.add_box((0.0, 0.0, 0.055), (1.00, 0.075, 0.055), PAL["ironwood_heavy"], bevel_amt=0.007)
    mb.add_box((0.0, 0.0, 1.945), (1.00, 0.075, 0.055), PAL["ironwood_heavy"], bevel_amt=0.007)
    mb.add_box((-0.94, 0.0, 1.00), (0.06, 0.075, 0.89), PAL["ironwood_heavy"], bevel_amt=0.007)
    mb.add_box((0.94, 0.0, 1.00), (0.06, 0.075, 0.89), PAL["ironwood_heavy"], bevel_amt=0.007)
    # Alt parapet (Z in [0.11..0.76]) ve ust lento (Z in [1.66..1.89])
    mb.add_box((0.0, 0.0, 0.435), (0.88, 0.055, 0.325), PAL["oak_plank_mid"], bevel_amt=0.008)
    mb.add_box((0.0, 0.0, 1.775), (0.88, 0.055, 0.115), PAL["oak_plank_mid"], bevel_amt=0.008)
    mb.add_box((-0.71, 0.0, 1.21), (0.17, 0.055, 0.45), PAL["oak_plank_dark"], bevel_amt=0.008)
    mb.add_box((0.71, 0.0, 1.21), (0.17, 0.055, 0.45), PAL["oak_plank_dark"], bevel_amt=0.008)

    # Tas denizlik (sill, Z=0.78m) + pencere sovesi + lento (X in [-0.54..+0.54], Z in [0.78..1.64])
    mb.add_box((0.0, -0.02, 0.78), (0.60, 0.105, 0.042), PAL["hearth_stone_warm"], bevel_amt=0.008)
    mb.add_box((0.0, 0.0, 1.64), (0.58, 0.088, 0.045), PAL["ironwood_heavy"], bevel_amt=0.007)
    mb.add_box((-0.51, 0.0, 1.21), (0.042, 0.082, 0.39), PAL["ironwood_heavy"], bevel_amt=0.006)
    mb.add_box((0.51, 0.0, 1.21), (0.042, 0.082, 0.39), PAL["ironwood_heavy"], bevel_amt=0.006)

    # Mimari Cam Panel (Architectural Glass) + orta kayit (mullion) ve kursun kafes citasi
    mb.add_box((0.0, 0.0, 1.21), (0.46, 0.014, 0.38), PAL["arch_glass"], bevel_amt=0.002)
    mb.add_box((0.0, 0.0, 1.21), (0.024, 0.038, 0.39), PAL["ironwood_heavy"], bevel_amt=0.004)
    mb.add_box((0.0, 0.0, 1.21), (0.46, 0.024, 0.016), PAL["lead_came"], bevel_amt=0.002)

    # Dis cepheye (-Y) acilmis sol ve sag Ahsap Kepenk Kanatlari (Wooden Shutters) + demir menteseler
    for sign in (-1.0, 1.0):
        mb.add_box(
            (sign * 0.73, -0.095, 1.21),
            (0.22, 0.024, 0.39),
            PAL["oak_plank_warm"],
            rot_euler=(0.0, 0.0, sign * 0.18),
            bevel_amt=0.006,
        )
        for hz in (0.94, 1.48):
            mb.add_box(
                (sign * 0.64, -0.098, hz),
                (0.14, 0.010, 0.022),
                PAL["wrought_iron"],
                rot_euler=(0.0, 0.0, sign * 0.18),
                bevel_amt=0.003,
            )
    return mb.finalize_object(mat)


def build_mod_stairs_straight_2m(mat):
    """
    1.36m genislik x 2.0m derinlik x 2.0m yukseklikte (45° akici egim) iki yan limon kirisi,
    8 kalin mese basamak, alt/ust korkuluk babalari ve 0.95m tirabzanli Duz Ahsap Merdiven.
    """
    mb = InteriorSiteMeshBuilder("Mod_Stairs_Straight_2m")
    # Iki yan 45° egimli kalin limon kirisi (Stringers: Y=+0.92,Z=0.10 -> Y=-0.92,Z=1.94)
    for sx in (-0.62, 0.62):
        mb.add_box(
            (sx, 0.0, 1.00),
            (0.055, 1.32, 0.11),
            PAL["ironwood_heavy"],
            rot_euler=(-0.785398, 0.0, 0.0),
            bevel_amt=0.008,
        )
        # Alt ve ust korkuluk babalari (Newel Posts) + egimli tirabzan (Handrail)
        mb.add_box((sx, 0.88, 0.52), (0.058, 0.058, 0.52), PAL["ironwood_heavy"], bevel_amt=0.007)
        mb.add_box((sx, -0.88, 1.42), (0.058, 0.058, 1.42), PAL["ironwood_heavy"], bevel_amt=0.007)
        mb.add_box(
            (sx, 0.0, 1.88),
            (0.042, 1.28, 0.042),
            PAL["oak_plank_warm"],
            rot_euler=(-0.785398, 0.0, 0.0),
            bevel_amt=0.006,
        )

    # 8 adet yatay mese basamak (Treads: Z = 0.25 .. 2.00m)
    steps = 8
    tones = (PAL["oak_plank_warm"], PAL["oak_plank_mid"], PAL["oak_plank_dark"])
    for s in range(steps):
        t = (s + 0.5) / steps
        cy = 0.88 - t * 1.76
        cz = 0.14 + (s + 1) * (1.84 / steps)
        mb.add_box((0.0, cy, cz), (0.58, 0.145, 0.032), tones[s % len(tones)], bevel_amt=0.006)
    return mb.finalize_object(mat)


def build_mod_stairs_spiral_stone(mat):
    """
    2.0m cap (R=1.0m) x 3.0m kat yuksekliginde merkez yontma tas sutun (Newel) etrafinda
    360° donen 12 kama basamakli ve dis korkuluk babali Tas Sarmal Merdiven.
    """
    mb = InteriorSiteMeshBuilder("Mod_Stairs_Spiral_Stone")
    # Merkez tasiyici tas cekirdek sutunu (Newel Pillar, Ø 0.44m, Z in [0.0..3.0m])
    mb.add_box((0.0, 0.0, 0.12), (0.28, 0.28, 0.12), PAL["hearth_stone_dark"], bevel_amt=0.012)
    mb.add_box((0.0, 0.0, 1.50), (0.21, 0.21, 1.50), PAL["ashlar_block"], bevel_amt=0.024)

    # 12 adet 30° donen yontma tas kama basamak + dis korkuluk dikmeleri
    steps = 12
    tones = (PAL["ashlar_block"], PAL["hearth_stone_warm"], PAL["lime_mortar"])
    for i in range(steps):
        ang = (2.0 * math.pi * i) / steps
        cz = (i + 1) * (2.88 / steps)
        r_mid = 0.56
        px = math.cos(ang) * r_mid
        py = math.sin(ang) * r_mid
        # Kama basamak blogu
        mb.add_box(
            (px, py, cz - 0.08),
            (0.42, 0.22, 0.115),
            tones[i % len(tones)],
            rot_euler=(0.0, 0.0, ang),
            bevel_amt=0.012,
        )
        # Dis kenar demir/tas korkuluk dikmesi ve tirabzan baglantisi
        rx = math.cos(ang) * 0.90
        ry = math.sin(ang) * 0.90
        mb.add_box((rx, ry, cz + 0.36), (0.035, 0.035, 0.42), PAL["wrought_iron"], bevel_amt=0.004)
        mb.add_box(
            (rx, ry, cz + 0.78),
            (0.04, 0.28, 0.035),
            PAL["wrought_iron_dark"],
            rot_euler=(0.32, 0.0, ang),
            bevel_amt=0.005,
        )
    return mb.finalize_object(mat)


def build_mod_fireplace_hearth(mat):
    """
    1.68m genislik x 1.08m derinlik x 2.00m yukseklikte yanmaz tas/tugla tabanli,
    kemerli ates hazneli, icinde kor odun yatagi ve konik davlumbazli Somine & Ocak.
    """
    mb = InteriorSiteMeshBuilder("Mod_Fireplace_Hearth")
    # 1. Yanmaz tas/tugla izolasyon taban plagi (Hearth Slab, Z in [0.0..0.16])
    mb.add_box((0.0, 0.0, 0.08), (0.84, 0.54, 0.08), PAL["hearth_stone_dark"], bevel_amt=0.014)
    # 2. Arka yangin duvari + sol/sag tas yanak ayaklari (Z in [0.16..1.26])
    mb.add_box((0.0, 0.32, 0.71), (0.78, 0.18, 0.55), PAL["hearth_stone_warm"], bevel_amt=0.014)
    mb.add_box((0.0, 0.14, 0.62), (0.48, 0.04, 0.42), PAL["soot_char"], bevel_amt=0.006)
    for sign in (-1.0, 1.0):
        mb.add_box((sign * 0.60, -0.02, 0.71), (0.20, 0.38, 0.55), PAL["fired_brick"], bevel_amt=0.012)
        mb.add_box((sign * 0.60, -0.02, 0.44), (0.21, 0.39, 0.12), PAL["ashlar_block"], bevel_amt=0.010)
        mb.add_box((sign * 0.60, -0.02, 0.96), (0.21, 0.39, 0.12), PAL["ashlar_block"], bevel_amt=0.010)

    # 3. Ates haznesi icinde demir izgara + korlasmis koz odunlar (Ember Glow)
    mb.add_box((0.0, -0.02, 0.21), (0.34, 0.22, 0.05), PAL["ember_glow"], bevel_amt=0.008)
    for lx, rot_z in ((-0.12, 0.25), (0.14, -0.30)):
        mb.add_box(
            (lx, -0.02, 0.28),
            (0.24, 0.055, 0.055),
            PAL["soot_char"],
            rot_euler=(0.0, 0.0, rot_z),
            bevel_amt=0.006,
        )

    # 4. Kalin somine rafi (Mantelpiece, Z=1.32m) + ust konik baca davlumbazi (Z in [1.38..2.00])
    mb.add_box((0.0, -0.04, 1.32), (0.86, 0.46, 0.07), PAL["ironwood_heavy"], bevel_amt=0.010)
    mb.add_box((0.0, 0.06, 1.56), (0.66, 0.36, 0.18), PAL["hearth_stone_warm"], bevel_amt=0.014)
    mb.add_box((0.0, 0.12, 1.86), (0.44, 0.32, 0.14), PAL["fired_brick_dark"], bevel_amt=0.012)
    return mb.finalize_object(mat)


def build_mod_chimney_flue_2m(mat):
    """
    0.78m x 0.78m kesit x 2.00m yukseklikte alt yanmaz tas yakali (Flue Collar),
    harcli tugla/tas govdeli ve ust yagmur/kar kesici sapkali Baca Safti (Chimney Flue).
    """
    mb = InteriorSiteMeshBuilder("Mod_Chimney_Flue_2m")
    # Alt doseme/cati gecisi yanmaz tas yakasi (Flue Collar, Z in [0.0..0.18])
    mb.add_box((0.0, 0.0, 0.09), (0.44, 0.44, 0.09), PAL["hearth_stone_dark"], bevel_amt=0.012)
    # Ana harcli pismis tugla ve kesme tas baca safti (Z in [0.18..1.64])
    mb.add_box((0.0, 0.0, 0.91), (0.36, 0.36, 0.73), PAL["fired_brick"], bevel_amt=0.014)
    for kz in (0.48, 0.92, 1.36):
        mb.add_box((0.0, 0.0, kz), (0.375, 0.375, 0.065), PAL["ashlar_block"], bevel_amt=0.008)
    # Ust kornis + isli duman agzi + 4 ayakli yagmur/kar kesici tas sapka (Z in [1.64..2.00])
    mb.add_box((0.0, 0.0, 1.66), (0.42, 0.42, 0.045), PAL["hearth_stone_warm"], bevel_amt=0.010)
    mb.add_box((0.0, 0.0, 1.71), (0.24, 0.24, 0.025), PAL["soot_char"], bevel_amt=0.004)
    for sx in (-0.28, 0.28):
        for sy in (-0.28, 0.28):
            mb.add_box((sx, sy, 1.81), (0.055, 0.055, 0.11), PAL["fired_brick_dark"], bevel_amt=0.006)
    mb.add_box((0.0, 0.0, 1.95), (0.46, 0.46, 0.05), PAL["hearth_stone_dark"], bevel_amt=0.012)
    return mb.finalize_object(mat)


def build_mod_scaffolding_frame_2m(mat):
    """
    2.0m x 2.0m x 2.0m kolay sokulen (%100 iadeli) Ahsap Santiye Iskelesi:
    4 kose sirik dikme, halat sargili X-payandalar, ust kalas platform, korkuluk ve ic merdiven.
    """
    mb = InteriorSiteMeshBuilder("Mod_Scaffolding_Frame_2m")
    # 4 kose soyulmus sirik dikme (X = ±0.92, Y = ±0.92, Z in [0.0..2.0m]) + halat bogumlari
    for sx in (-0.92, 0.92):
        for sy in (-0.92, 0.92):
            mb.add_box((sx, sy, 1.00), (0.052, 0.052, 1.00), PAL["peeled_pole"], bevel_amt=0.008)
            for lz in (0.18, 1.38, 1.92):
                mb.add_box((sx, sy, lz), (0.064, 0.064, 0.035), PAL["hemp_rope"], bevel_amt=0.004)

    # Alt ve ust yatay tasiyici kusaklar (Z=0.18m ve Z=1.38m platform seviyesi)
    for kz in (0.18, 1.38):
        for s in (-0.92, 0.92):
            mb.add_box((0.0, s, kz), (0.94, 0.042, 0.042), PAL["oak_plank_mid"], bevel_amt=0.005)
            mb.add_box((s, 0.0, kz), (0.042, 0.94, 0.042), PAL["oak_plank_mid"], bevel_amt=0.005)

    # Arka (+Y) ve yanlarda capraz X-gogusleme siriklari + ust koruyucu korkuluk (Z=1.92m)
    mb.add_box(
        (0.0, 0.92, 0.78),
        (0.032, 0.032, 0.82),
        PAL["peeled_pole"],
        rot_euler=(0.0, 0.92, 0.0),
        bevel_amt=0.004,
    )
    mb.add_box(
        (0.0, 0.92, 0.78),
        (0.032, 0.032, 0.82),
        PAL["peeled_pole"],
        rot_euler=(0.0, -0.92, 0.0),
        bevel_amt=0.004,
    )
    for s in (-0.92, 0.92):
        mb.add_box((0.0, s, 1.92), (0.94, 0.038, 0.038), PAL["peeled_pole"], bevel_amt=0.005)
        mb.add_box((s, 0.0, 1.92), (0.038, 0.94, 0.038), PAL["peeled_pole"], bevel_amt=0.005)

    # Z=1.44m seviyesinde yurunebilir 5 kalasli calisma platformu (merdiven cikis bosluklu)
    for idx, px in enumerate((-0.72, -0.42, -0.12, 0.18)):
        col = PAL["oak_plank_warm"] if (idx % 2 == 0) else PAL["oak_plank_mid"]
        mb.add_box((px, 0.0, 1.44), (0.135, 0.95, 0.024), col, bevel_amt=0.005)

    # Sag bolmede (X=+0.60m) entegre tirmanilabilir egimli ahsap iskele merdiveni
    for lx in (0.44, 0.78):
        mb.add_box(
            (lx, 0.0, 0.76),
            (0.028, 0.038, 0.86),
            PAL["ironwood_heavy"],
            rot_euler=(-0.54, 0.0, 0.0),
            bevel_amt=0.004,
        )
    for r in range(6):
        t = (r + 0.5) / 6.0
        ry = 0.42 - t * 0.84
        rz = 0.16 + t * 1.24
        mb.add_box((0.61, ry, rz), (0.18, 0.024, 0.024), PAL["peeled_pole"], bevel_amt=0.003)
    return mb.finalize_object(mat)


def build_mod_pulley_crane_3m(mat):
    """
    1.80m x 2.20m taban x 3.00m yukseklikte 360° doner kollu Makarali Santiye Vinci (Pulley Crane):
    agir capraz kaide, tas denge kasasi, doner direk, egimli bum, cikrik, makara ve demir kanca.
    """
    mb = InteriorSiteMeshBuilder("Mod_Pulley_Crane_3m")
    # 1. Alt capraz ahsap kaide + payandalar (Z in [0.0..0.24])
    mb.add_box((0.0, 0.0, 0.08), (0.88, 0.14, 0.08), PAL["ironwood_heavy"], bevel_amt=0.008)
    mb.add_box((0.0, 0.0, 0.08), (0.14, 0.96, 0.08), PAL["ironwood_heavy"], bevel_amt=0.008)
    mb.add_box((0.0, 0.0, 0.22), (0.26, 0.26, 0.08), PAL["wrought_iron_dark"], bevel_amt=0.006)

    # 2. Dikey doner ana vinç diregi (Mast, Z in [0.16..2.58]) + arka tas denge agirligi kasasi
    mb.add_box((0.0, 0.0, 1.38), (0.11, 0.11, 1.20), PAL["oak_plank_warm"], bevel_amt=0.010)
    mb.add_box((0.0, 0.58, 0.36), (0.34, 0.32, 0.22), PAL["oak_plank_dark"], bevel_amt=0.008)
    mb.add_box((0.0, 0.58, 0.54), (0.28, 0.26, 0.10), PAL["hearth_stone_warm"], bevel_amt=0.010)

    # 3. Cikrik tamburu (Windlass Drum, Z=0.96m) ve sarili halat
    mb.add_box((0.0, 0.22, 0.96), (0.34, 0.085, 0.085), PAL["hemp_rope"], bevel_amt=0.008)
    mb.add_box((0.0, 0.22, 0.96), (0.44, 0.032, 0.032), PAL["wrought_iron"], bevel_amt=0.004)

    # 4. One (-Y) uzanan egimli vinc kolu (Jib Boom) + alt destek payandasi (Z max = 3.00m)
    mb.add_box(
        (0.0, -0.46, 2.38),
        (0.085, 0.88, 0.085),
        PAL["oak_plank_warm"],
        rot_euler=(0.52, 0.0, 0.0),
        bevel_amt=0.008,
    )
    mb.add_box(
        (0.0, -0.34, 1.72),
        (0.065, 0.52, 0.065),
        PAL["ironwood_heavy"],
        rot_euler=(0.82, 0.0, 0.0),
        bevel_amt=0.006,
    )

    # 5. Uc makara carki (Pulley Sheave, Y=-1.06, Z=2.78), sarkitilmis halat ve demir kanca (Hoist Hook)
    mb.add_box((0.0, -1.06, 2.78), (0.048, 0.14, 0.14), PAL["brass_ring"], bevel_amt=0.006)
    mb.add_box((0.0, -1.06, 2.80), (0.072, 0.065, 0.18), PAL["wrought_iron_dark"], bevel_amt=0.005)
    mb.add_box((0.0, -1.06, 2.02), (0.022, 0.022, 0.64), PAL["hemp_rope"], bevel_amt=0.003)
    mb.add_box((0.0, -1.06, 1.32), (0.065, 0.065, 0.085), PAL["wrought_iron_dark"], bevel_amt=0.006)
    mb.add_box((0.0, -1.02, 1.18), (0.032, 0.085, 0.075), PAL["wrought_iron"], bevel_amt=0.005)
    return mb.finalize_object(mat)


def build_mod_site_material_pile(mat):
    """
    2.0m x 2.0m x 1.15m Santiye Malzeme Yigini (Site Material Pile, r <= 8.0m / 14.0m besleme):
    ahsap palet ustunde soyulmus kutuk istifi, kalas destesi, kesme tas/tugla paleti ve harc teknesi.
    """
    mb = InteriorSiteMeshBuilder("Mod_Site_Material_Pile")
    # 1. Alt ahsap santiye izgarasi / palet (Z in [0.0..0.10], 1.94m x 1.94m)
    for py in (-0.78, 0.0, 0.78):
        mb.add_box((0.0, py, 0.04), (0.95, 0.075, 0.04), PAL["ironwood_heavy"], bevel_amt=0.006)
    for px in (-0.76, -0.25, 0.25, 0.76):
        mb.add_box((px, 0.0, 0.095), (0.18, 0.94, 0.020), PAL["oak_plank_dark"], bevel_amt=0.005)

    # 2. Sol tarafta (X < 0) piramit istiflenmis 3+2 soyulmus kutuk + halat kusagi
    for lx in (-0.74, -0.46, -0.18):
        mb.add_box((lx, 0.10, 0.24), (0.125, 0.78, 0.125), PAL["peeled_pole"], bevel_amt=0.028)
    for lx in (-0.60, -0.32):
        mb.add_box((lx, 0.10, 0.46), (0.125, 0.76, 0.125), PAL["oak_plank_warm"], bevel_amt=0.028)
    mb.add_box((-0.46, 0.10, 0.68), (0.125, 0.74, 0.125), PAL["peeled_pole"], bevel_amt=0.028)
    for ry in (-0.42, 0.62):
        mb.add_box((-0.46, ry, 0.36), (0.42, 0.028, 0.26), PAL["hemp_rope"], bevel_amt=0.004)

    # 3. Sag arka kosede (X > 0.15, Y > 0) kesme tas bloklari ve pismis tugla istifi
    stone_cols = (PAL["ashlar_block"], PAL["hearth_stone_warm"], PAL["fired_brick"])
    for r in range(3):
        for c in range(2):
            sx = 0.32 + c * 0.38
            sz = 0.22 + r * 0.22
            mb.add_box(
                (sx, 0.38, sz),
                (0.175, 0.36, 0.102),
                stone_cols[(r + c) % len(stone_cols)],
                bevel_amt=0.010,
            )

    # 4. Sag on kosede (X > 0.15, Y < 0) kirec harci teknesi (Mortar Trough) + demir mala
    mb.add_box((0.50, -0.52, 0.22), (0.38, 0.28, 0.12), PAL["oak_plank_dark"], bevel_amt=0.008)
    mb.add_box((0.50, -0.52, 0.33), (0.32, 0.22, 0.035), PAL["lime_mortar"], bevel_amt=0.006)
    mb.add_box(
        (0.56, -0.46, 0.39),
        (0.085, 0.048, 0.016),
        PAL["wrought_iron"],
        rot_euler=(0.15, -0.25, 0.40),
        bevel_amt=0.003,
    )
    return mb.finalize_object(mat)


def export_single_piece_glb(obj, out_glb_path):
    os.makedirs(os.path.dirname(out_glb_path), exist_ok=True)
    saved_loc = obj.location.copy()
    saved_rot = obj.rotation_euler.copy()
    obj.location = Vector((0.0, 0.0, 0.0))
    obj.rotation_euler = Euler((0.0, 0.0, 0.0), "XYZ")

    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)

    bpy.ops.export_scene.gltf(
        filepath=out_glb_path,
        export_format="GLB",
        use_selection=True,
        export_yup=True,
        export_apply=False,
        export_normals=True,
        export_attributes=True,
        export_materials="NONE",
        export_texcoords=False,
        export_cameras=False,
        export_lights=False,
    )

    obj.location = saved_loc
    obj.rotation_euler = saved_rot


def main():
    _ = sys.argv
    clear_scene()
    mat = create_preview_vertex_color_material()

    interior_site_specs = [
        ("mod_hinged_door_leaf", build_mod_hinged_door_leaf),
        ("mod_double_gate_leaf", build_mod_double_gate_leaf),
        ("mod_shuttered_window_2x2m", build_mod_shuttered_window_2x2m),
        ("mod_stairs_straight_2m", build_mod_stairs_straight_2m),
        ("mod_stairs_spiral_stone", build_mod_stairs_spiral_stone),
        ("mod_fireplace_hearth", build_mod_fireplace_hearth),
        ("mod_chimney_flue_2m", build_mod_chimney_flue_2m),
        ("mod_scaffolding_frame_2m", build_mod_scaffolding_frame_2m),
        ("mod_pulley_crane_3m", build_mod_pulley_crane_3m),
        ("mod_site_material_pile", build_mod_site_material_pile),
    ]

    for asset_id, builder_fn in interior_site_specs:
        obj = builder_fn(mat)
        out_glb = os.path.join(OUT_MODULAR_DIR, f"{asset_id}.glb")
        export_single_piece_glb(obj, out_glb)
        tris = len(obj.data.polygons)
        size_kb = os.path.getsize(out_glb) / 1024.0
        print(f"[InteriorSiteKitBuilder] Exported {asset_id}.glb ({tris} tris, {size_kb:.1f} KB)")


if __name__ == "__main__":
    main()
