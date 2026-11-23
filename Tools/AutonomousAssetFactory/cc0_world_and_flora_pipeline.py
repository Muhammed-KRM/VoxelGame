"""
Stratocraft Otonom 3B Fabrika — Gercek Nordik Kutuk-Tas Yapi Kiti (13 Parca + 45° Gable Wall)
ve Organik 3B Cam / Karli Cam / Mese Agac Taci & Kok Mahmuzu Ureticisi (Blender 4.2 LTS).

Kullanim (PATH uzerindeki blender ile, 0 gomulu doku, UV-free dogrudan RGB COLOR_0):
    blender --background --python Tools/AutonomousAssetFactory/cc0_world_and_flora_pipeline.py

Uretilen 13 Moduler Yapi .glb Parcasi (Assets/Art/Generated/Structures_Modular/*.glb):
    1.  mod_log_beam_2m.glb            — Yontulmus damarli yuvarlak Nordik kutuk kiris (2.08m)
    2.  mod_log_pillar_3m.glb          — Eyer centikli tasiyici yuvarlak kutuk dikme (3.00m)
    3.  mod_plank_wall_2x2m.glb        — Gecmeli Nordik yatay kutuk + kalas duvar (2x2m)
    4.  mod_plank_doorway_2x2m.glb     — Kemer takozlu & demir menteseli Nordik kapili duvar (2x2m)
    5.  mod_plank_window_2x2m.glb      — Disa acilir ahsap kepenkli & denizlikli pencereli duvar (2x2m)
    6.  mod_plank_floor_2x2m.glb       — Alt kirisli kalin yontma ahsap doseme (2x2m)
    7.  mod_stone_foundation_2x2m.glb  — Derin egim etekli (-0.85m..+0.32m) harcli tas subasman (2x2m)
    8.  mod_stone_arch_pillar_3m.glb   — Duz abakus tablali & kavisli konsollu tas kemer sutunu (3.00m)
    9.  mod_roof_rafter_26deg_2x2m.glb — 26° kalin kademeli kiremit/zona kapli cati (2x2m)
    10. mod_roof_rafter_45deg_2x2m.glb — 45° dik kademeli Nordik zona/arduvaz cati (2x2m)
    11. mod_roof_ridge_2m.glb          — Ters-V oymali Nordik cati mahya kirisi (2.04m)
    12. mod_diagonal_brace_2m.glb      — 45° demir kusakli capraz gogusleme payandasi (2.00m)
    13. mod_gable_wall_45deg_2m.glb    — 45° ucgen cati alinlik duvari (GableWall45Deg2m, 2x2m dik ucgen)

Uretilen 4 Organik Flora .glb Parcasi (Assets/Art/Generated/Flora/*.glb):
    1.  flora_nordic_pine_canopy.glb   — Cok katmanli sarkik igne yaprak salkimli + konik tepeli cam taci
    2.  flora_snow_pine_canopy.glb     — Ust dal kumeleri kar yastikli Nordik buzul cami taci
    3.  flora_ancient_oak_canopy.glb   — Cok-kumeli organik dalli kadim mese agaci taci
    4.  flora_tree_root_buttress.glb   — Egim telafili kok mahmuzlari & yosunlu govde tabani
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, "..", ".."))
OUT_MODULAR_DIR = os.path.join(REPO_ROOT, "Assets", "Art", "Generated", "Structures_Modular")
OUT_FLORA_DIR = os.path.join(REPO_ROOT, "Assets", "Art", "Generated", "Flora")
OUT_PREVIEW_DIR = os.path.join(REPO_ROOT, "BuildLogs", "Previews")


# Mat dogal Nordik ahsap, harcli tas, zona ve organik cam/mese yaprak COLOR_0 paleti (sRGB 0..255)
# Not: R kanali 0, 3 veya 11 degerlerinden kacinir (StratocraftUberShader ozel maske ayrimi).
PAL = {
    # Nordik kutuk & ahsap damar tonlari
    "log_peeled_warm": (182, 134, 82, 246),
    "log_peeled_mid": (164, 118, 70, 242),
    "log_peeled_shade": (136, 94, 54, 224),
    "log_grain_dark": (118, 80, 46, 218),
    "log_notch_carve": (196, 152, 98, 236),
    "log_endgrain_outer": (204, 164, 110, 248),
    "log_endgrain_heart": (148, 102, 58, 240),
    "chink_moss_mortar": (86, 84, 68, 198),
    "bark_dark": (72, 50, 36, 218),
    "bark_ridge": (92, 64, 44, 228),
    "bark_moss": (68, 82, 46, 226),
    # Kalas, kepenk, karkas ve doseme
    "plank_light": (166, 120, 74, 244),
    "plank_mid": (148, 104, 62, 240),
    "plank_warm": (158, 110, 66, 242),
    "plank_dark": (122, 84, 48, 228),
    "shutter_pine": (134, 92, 54, 238),
    "shutter_trim": (104, 70, 40, 232),
    "stud_frame": (108, 72, 42, 232),
    "sill_weathered": (118, 88, 60, 236),
    "joist_heavy": (98, 66, 38, 222),
    # Dovme demir menteseler, kusaklar ve ahsap kavelalar
    "iron_hardware": (58, 62, 68, 236),
    "wood_peg": (182, 138, 84, 244),
    # Harcli tarla tasi subasman ve yontma kemer tasi
    "mortar_bed": (106, 104, 98, 192),
    "stone_warm": (142, 134, 122, 242),
    "stone_cool": (124, 128, 134, 240),
    "stone_dark": (92, 94, 98, 224),
    "stone_deep_skirt": (78, 80, 84, 214),
    "stone_light": (160, 154, 144, 246),
    "stone_moss_tint": (98, 110, 86, 228),
    "arch_carved": (166, 160, 150, 246),
    "arch_keystone": (180, 172, 160, 250),
    # Kalin kademeli cati kiremidi / katranli cam & arduvaz zona
    "shingle_dark": (62, 48, 38, 236),
    "shingle_mid": (78, 60, 46, 242),
    "shingle_warm": (92, 70, 52, 244),
    "shingle_slate": (68, 72, 78, 240),
    "shingle_moss": (72, 82, 56, 234),
    "rafter_timber": (128, 88, 52, 234),
    # Organik Cam & Mese Yaprak ve Kar Paleti (mat dogal klorofil tonlari)
    "pine_deep": (28, 52, 32, 206),
    "pine_mid": (40, 74, 42, 232),
    "pine_sun": (58, 98, 50, 244),
    "pine_tip": (74, 116, 58, 248),
    "pine_cold": (34, 62, 46, 226),
    "snow_bright": (224, 234, 244, 250),
    "snow_shade": (176, 194, 216, 238),
    "oak_deep": (34, 60, 28, 202),
    "oak_mid": (52, 92, 38, 234),
    "oak_sun": (76, 120, 46, 246),
    "oak_warm_tip": (98, 130, 52, 248),
}


def srgb_to_linear_rgba(rgba_u8):
    """Blender 4.2 loop color -> glTF COLOR_0 icin kesin sRGB->Linear donusumu."""

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
    mat = bpy.data.materials.new(name="M_CC0WorldFloraPreview")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    vcol = nt.nodes.new("ShaderNodeVertexColor")
    vcol.layer_name = "Col"
    bsdf.inputs["Roughness"].default_value = 0.88
    bsdf.inputs["Specular IOR Level"].default_value = 0.12
    nt.links.new(vcol.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


class SculptedMeshBuilder:
    """
    UV-free COLOR_0 mimari ve organik flora bmesh insacisi.
    Koordinat eslemesi (export_yup=True + LoadArticulatedPartsFromDisk / OrganicTreeCanopyVisualSystem):
      Blender +Z -> Unity +Y (Yukari)
      Blender -Y -> Unity +Z (Ileri)
      Blender -X -> Unity +X (Sag)
    """

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.color_layer = self.bm.loops.layers.color.new("Col")

    def paint_faces(
        self,
        faces,
        rgba_u8,
        z_ao_origin=0.0,
        z_ao_span=2.0,
        z_ao_strength=0.12,
        grain_freq=0.0,
    ):
        lin = srgb_to_linear_rgba(rgba_u8)
        for f in faces:
            if not f.is_valid:
                continue
            ctr = f.calc_center_median()
            cz = ctr.z
            nrm = f.normal
            t_h = max(0.0, min(1.0, (cz - z_ao_origin) / max(0.25, z_ao_span)))
            shade = (1.0 - z_ao_strength * 0.62) + z_ao_strength * t_h
            if nrm.z < -0.42:
                shade *= 0.82
            elif nrm.z > 0.52:
                shade *= 1.06
            if grain_freq > 0.01:
                g_wave = math.sin(ctr.x * grain_freq + ctr.y * grain_freq * 0.7 + ctr.z * 3.1)
                shade *= 1.0 + 0.065 * g_wave
            ao = max(0.50, min(1.0, lin[3] * (0.86 + 0.14 * t_h)))
            col = (
                max(0.012, min(1.0, lin[0] * shade)),
                max(0.012, min(1.0, lin[1] * shade)),
                max(0.012, min(1.0, lin[2] * shade)),
                ao,
            )
            for loop in f.loops:
                loop[self.color_layer] = col

    def add_box(
        self,
        center,
        half_extents,
        rgba_u8,
        rot_euler=(0.0, 0.0, 0.0),
        bevel_amt=0.008,
        grain_freq=0.0,
    ):
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
        self.paint_faces(new_faces, rgba_u8, grain_freq=grain_freq)
        return new_faces

    def add_trapezoid_prism_x(
        self,
        x_bot_min,
        x_bot_max,
        x_top_min,
        x_top_max,
        z_bot,
        z_top,
        y_center,
        half_y,
        rgba_u8,
        bevel_amt=0.004,
    ):
        """45° ucgen alinlik icindeki kesik kutuk/kalas siralari icin kesin yamuk prizma uretir."""
        if x_bot_max - x_bot_min < 0.025 or x_top_max - x_top_min < 0.015 or z_top <= z_bot + 0.01:
            return []
        prev_faces = set(self.bm.faces)
        y0 = y_center - half_y
        y1 = y_center + half_y
        coords = [
            (x_bot_min, y0, z_bot),
            (x_bot_max, y0, z_bot),
            (x_top_max, y0, z_top),
            (x_top_min, y0, z_top),
            (x_bot_min, y1, z_bot),
            (x_bot_max, y1, z_bot),
            (x_top_max, y1, z_top),
            (x_top_min, y1, z_top),
        ]
        vs = [self.bm.verts.new(c) for c in coords]
        quads = [
            (vs[0], vs[3], vs[2], vs[1]),
            (vs[4], vs[5], vs[6], vs[7]),
            (vs[0], vs[1], vs[5], vs[4]),
            (vs[3], vs[7], vs[6], vs[2]),
            (vs[0], vs[4], vs[7], vs[3]),
            (vs[1], vs[2], vs[6], vs[5]),
        ]
        for q in quads:
            try:
                self.bm.faces.new(q)
            except ValueError:
                pass
        new_faces = [f for f in self.bm.faces if f not in prev_faces and f.is_valid]
        if bevel_amt > 0.0 and new_faces:
            edges = list({e for f in new_faces for e in f.edges})
            bmesh.ops.bevel(self.bm, geom=edges, offset=bevel_amt, segments=1, affect="EDGES")
            new_faces = [f for f in self.bm.faces if f not in prev_faces and f.is_valid]
        bmesh.ops.recalc_face_normals(self.bm, faces=new_faces)
        self.paint_faces(new_faces, rgba_u8, z_ao_origin=0.0, z_ao_span=2.0, grain_freq=11.0)
        return new_faces

    def add_irregular_stone(self, center, radii, rgba_u8, seed_val, rot_euler=(0.0, 0.0, 0.0)):
        """Cok-yuzeyli yontulmus / kirik dogal tarla tasi blogu uretir."""
        prev_faces = set(self.bm.faces)
        prev_verts = set(self.bm.verts)
        rot_m = Euler(rot_euler, "XYZ").to_matrix().to_4x4()
        scale_m = Matrix.Diagonal((radii[0] * 2.0, radii[1] * 2.0, radii[2] * 2.0, 1.0))
        xform = Matrix.Translation(Vector(center)) @ rot_m @ scale_m
        bmesh.ops.create_cube(self.bm, size=1.0, matrix=xform)
        cur_faces = [f for f in self.bm.faces if f not in prev_faces]
        edges = list({e for f in cur_faces if f.is_valid for e in f.edges})
        bevel_w = min(radii) * 0.30
        bmesh.ops.bevel(self.bm, geom=edges, offset=bevel_w, segments=1, affect="EDGES")

        new_verts = [v for v in self.bm.verts if v not in prev_verts and v.is_valid]
        c_vec = Vector(center)
        for idx, v in enumerate(new_verts):
            rel = v.co - c_vec
            h = math.sin((idx + 1) * 12.9898 + seed_val * 78.233) * 43758.5453
            frac = h - math.floor(h)
            jitter = 0.89 + 0.20 * frac
            v.co = c_vec + Vector((rel.x * jitter, rel.y * jitter, rel.z * (0.92 + 0.14 * frac)))

        new_faces = [f for f in self.bm.faces if f not in prev_faces and f.is_valid]
        bmesh.ops.recalc_face_normals(self.bm, faces=new_faces)
        self.paint_faces(
            new_faces, rgba_u8, z_ao_origin=-0.85, z_ao_span=1.25, z_ao_strength=0.16, grain_freq=6.5
        )
        return new_faces

    def add_notched_log(
        self,
        length,
        radius,
        notch_positions,
        along_axis="X",
        center_offset=(0.0, 0.0, 0.0),
        segs=16,
    ):
        """Soyulmus damarli yuvarlak Nordik ladin/cam kutuk + eyer centikleri + yas halkali kesit."""
        half_l = length * 0.5
        num_base = 24
        ts = [(-half_l + (length * i) / num_base) for i in range(num_base + 1)]
        for np_pos in notch_positions:
            for dt in (-0.15, -0.09, 0.0, 0.09, 0.15):
                cand = max(-half_l + 0.02, min(half_l - 0.02, np_pos + dt))
                ts.append(cand)
        ts = sorted({round(v, 4) for v in ts})

        ring_verts = []
        ring_cols = []
        off = Vector(center_offset)

        for t in ts:
            dist_end = half_l - abs(t)
            is_bark_collar = 0.035 < dist_end < 0.115
            notch_depth = 0.0
            for np_pos in notch_positions:
                d = abs(t - np_pos)
                if d < 0.145:
                    notch_depth = max(notch_depth, math.cos((d / 0.145) * (math.pi * 0.5)))

            col = (
                PAL["bark_dark"]
                if is_bark_collar
                else (PAL["log_notch_carve"] if notch_depth > 0.35 else PAL["log_peeled_warm"])
            )
            ring_cols.append(col)

            rv = []
            for s in range(segs):
                ang = (2.0 * math.pi * s) / segs
                ca = math.cos(ang)
                sa = math.sin(ang)
                organic = (
                    1.0
                    + 0.030 * math.sin(ang * 3.0 + t * 4.2)
                    + 0.018 * math.cos(ang * 6.0 - t * 2.9)
                )
                r_local = radius * organic
                if is_bark_collar:
                    r_local *= 1.04
                elif dist_end < 0.015:
                    r_local *= 0.94

                if notch_depth > 0.01:
                    vert_align = abs(sa)
                    if vert_align > 0.36:
                        carve = ((vert_align - 0.36) / 0.64) * 0.35 * notch_depth
                        r_local *= 1.0 - carve

                if along_axis == "X":
                    pt = Vector((t, ca * r_local, sa * r_local)) + off
                else:
                    z_val = t + half_l
                    pt = Vector((ca * r_local, sa * r_local, z_val)) + off
                rv.append(self.bm.verts.new(pt))
            ring_verts.append(rv)

        for i in range(len(ring_verts) - 1):
            r0 = ring_verts[i]
            r1 = ring_verts[i + 1]
            for s in range(segs):
                sn = (s + 1) % segs
                try:
                    f = self.bm.faces.new((r0[s], r0[sn], r1[sn], r1[s]))
                    bmesh.ops.recalc_face_normals(self.bm, faces=[f])
                    base_c = ring_cols[i]
                    if base_c == PAL["log_peeled_warm"] and (s % 4 == 1):
                        base_c = PAL["log_peeled_mid"]
                    elif base_c == PAL["log_peeled_warm"] and (s % 7 == 0):
                        base_c = PAL["log_grain_dark"]
                    self.paint_faces([f], base_c, grain_freq=9.0)
                except ValueError:
                    pass

        for end_idx, flip in ((0, True), (-1, False)):
            outer_ring = ring_verts[end_idx]
            center_pt = sum((v.co for v in outer_ring), Vector((0.0, 0.0, 0.0))) / len(outer_ring)
            inner_ring = [self.bm.verts.new(center_pt.lerp(v.co, 0.48)) for v in outer_ring]
            c_vert = self.bm.verts.new(center_pt)
            outer_faces = []
            inner_faces = []
            for s in range(segs):
                sn = (s + 1) % segs
                try:
                    if flip:
                        fo = self.bm.faces.new((outer_ring[sn], outer_ring[s], inner_ring[s], inner_ring[sn]))
                        fi = self.bm.faces.new((inner_ring[sn], inner_ring[s], c_vert))
                    else:
                        fo = self.bm.faces.new((outer_ring[s], outer_ring[sn], inner_ring[sn], inner_ring[s]))
                        fi = self.bm.faces.new((inner_ring[s], inner_ring[sn], c_vert))
                    outer_faces.append(fo)
                    inner_faces.append(fi)
                except ValueError:
                    pass
            bmesh.ops.recalc_face_normals(self.bm, faces=outer_faces + inner_faces)
            self.paint_faces(outer_faces, PAL["log_endgrain_outer"])
            self.paint_faces(inner_faces, PAL["log_endgrain_heart"])

    def add_tapered_tube(self, points, radii, cols_u8, segs=8, cap_ends=True):
        """Organik agac govdesi, kivrimli dal ve kok mahmuzu icin konik cok-halkali tup."""
        if len(points) < 2:
            return
        rings = []
        for idx, (pt, rad) in enumerate(zip(points, radii)):
            p = Vector(pt)
            if idx == 0:
                tang = (Vector(points[1]) - p).normalized()
            elif idx == len(points) - 1:
                tang = (p - Vector(points[-2])).normalized()
            else:
                tang = (Vector(points[idx + 1]) - Vector(points[idx - 1])).normalized()
            if tang.length_squared < 1e-6:
                tang = Vector((0.0, 0.0, 1.0))
            ref = Vector((1.0, 0.0, 0.0)) if abs(tang.x) < 0.85 else Vector((0.0, 1.0, 0.0))
            n1 = tang.cross(ref).normalized()
            n2 = tang.cross(n1).normalized()
            rv = []
            for s in range(segs):
                ang = (2.0 * math.pi * s) / segs
                flute = 1.0 + 0.06 * math.sin(ang * 3.0 + idx * 1.3)
                offset = (n1 * math.cos(ang) + n2 * math.sin(ang)) * (rad * flute)
                rv.append(self.bm.verts.new(p + offset))
            rings.append(rv)

        for i in range(len(rings) - 1):
            r0 = rings[i]
            r1 = rings[i + 1]
            band = []
            for s in range(segs):
                sn = (s + 1) % segs
                try:
                    band.append(self.bm.faces.new((r0[s], r0[sn], r1[sn], r1[s])))
                except ValueError:
                    pass
            bmesh.ops.recalc_face_normals(self.bm, faces=band)
            self.paint_faces(band, cols_u8[min(i, len(cols_u8) - 1)], z_ao_origin=-0.6, z_ao_span=4.5)

        if cap_ends:
            for end_i, flip in ((0, True), (-1, False)):
                ring = rings[end_i]
                ctr = sum((v.co for v in ring), Vector((0.0, 0.0, 0.0))) / len(ring)
                cv = self.bm.verts.new(ctr)
                cap_f = []
                for s in range(segs):
                    sn = (s + 1) % segs
                    try:
                        if flip:
                            cap_f.append(self.bm.faces.new((ring[sn], ring[s], cv)))
                        else:
                            cap_f.append(self.bm.faces.new((ring[s], ring[sn], cv)))
                    except ValueError:
                        pass
                bmesh.ops.recalc_face_normals(self.bm, faces=cap_f)
                self.paint_faces(cap_f, cols_u8[end_i])

    def add_pine_bough_cluster(
        self,
        origin,
        dir_xy,
        length,
        width,
        droop,
        seed_val,
        snowy=False,
        low_poly=False,
        cap_root=True,
    ):
        """
        Asla kup olmayan, gercek sarkik cam igne yaprak salkimi (kavisli, tirtikli konik-yelpaze bough).
        Ust yuzey gunes alan cam yesili (veya karli camda kar yastigi), alt yuzey koyu golge igne yesili.
        """
        d2 = Vector((dir_xy[0], dir_xy[1], 0.0))
        if d2.length_squared < 1e-6:
            d2 = Vector((1.0, 0.0, 0.0))
        d2.normalize()
        perp = Vector((-d2.y, d2.x, 0.0))
        orig = Vector(origin)

        def _classify_bough_col(nz, band_idx):
            if snowy and nz > 0.22:
                return PAL["snow_bright"] if nz > 0.55 else PAL["snow_shade"]
            if snowy:
                return PAL["pine_cold"] if nz > -0.22 else PAL["pine_deep"]
            if nz > 0.40:
                return PAL["pine_tip"] if band_idx >= 1 else PAL["pine_sun"]
            if nz > -0.12:
                return PAL["pine_mid"]
            return PAL["pine_deep"]

        def _paint_bough_face(face, band_idx):
            bmesh.ops.recalc_face_normals(self.bm, faces=[face])
            col = _classify_bough_col(face.normal.z, band_idx)
            self.paint_faces([face], col, z_ao_origin=-1.2, z_ao_span=5.5, z_ao_strength=0.14)

        if low_poly:
            # 4 istasyon (kok -> genis omuz -> sarkik orta -> sivri uc) & 5 nokta profili (paylasilan vertex)
            stations = [
                (0.00, 0.14 * width, 0.045, 0.00),
                (0.32, 0.64 * width, 0.175, -0.14 * droop),
                (0.68, 0.48 * width, 0.135, -0.52 * droop),
            ]
            prof_angles = [
                (0.0, 1.0),
                (1.00, 0.16),
                (0.58, -0.74),
                (-0.58, -0.74),
                (-1.00, 0.16),
            ]
            ring_verts = []
            for st_i, (t, half_w, half_h, dz) in enumerate(stations):
                c_pt = orig + d2 * (t * length) + Vector((0.0, 0.0, dz))
                rv = []
                for p_i, (lat_k, up_k) in enumerate(prof_angles):
                    jag = 1.0 + 0.11 * math.sin(seed_val * 5.7 + st_i * 2.3 + p_i * 1.9)
                    pt = (
                        c_pt
                        + perp * (lat_k * half_w * jag)
                        + Vector((0.0, 0.0, up_k * half_h * jag))
                    )
                    rv.append(self.bm.verts.new(pt))
                ring_verts.append(rv)

            n_prof = len(prof_angles)
            bough_faces = []
            bough_verts = [v for rv in ring_verts for v in rv]
            for i in range(len(ring_verts) - 1):
                r0, r1 = ring_verts[i], ring_verts[i + 1]
                for s in range(n_prof):
                    sn = (s + 1) % n_prof
                    try:
                        f = self.bm.faces.new((r0[s], r0[sn], r1[sn], r1[s]))
                        f.smooth = True
                        bough_faces.append(f)
                    except ValueError:
                        pass

            tip_pt = orig + d2 * length + Vector((0.0, 0.0, -droop))
            tip_vert = self.bm.verts.new(tip_pt)
            bough_verts.append(tip_vert)
            r_last = ring_verts[-1]
            for s in range(n_prof):
                sn = (s + 1) % n_prof
                try:
                    f = self.bm.faces.new((r_last[s], r_last[sn], tip_vert))
                    f.smooth = True
                    bough_faces.append(f)
                except ValueError:
                    pass

            if cap_root:
                r0 = ring_verts[0]
                ctr = sum((v.co for v in r0), Vector((0.0, 0.0, 0.0))) / len(r0)
                cv = self.bm.verts.new(ctr)
                bough_verts.append(cv)
                for s in range(n_prof):
                    sn = (s + 1) % n_prof
                    try:
                        f = self.bm.faces.new((r0[sn], r0[s], cv))
                        f.smooth = True
                        bough_faces.append(f)
                    except ValueError:
                        pass

            bmesh.ops.recalc_face_normals(self.bm, faces=bough_faces)
            self.bm.normal_update()
            vert_cols = {}
            for v in bough_verts:
                nz = v.normal.z
                dist_ratio = max(0.0, min(1.0, (v.co - orig).xy.length / max(0.25, length)))
                col_u8 = _classify_bough_col(nz, 1 if dist_ratio > 0.42 else 0)
                lin = srgb_to_linear_rgba(col_u8)
                t_h = max(0.0, min(1.0, (v.co.z + 1.2) / 5.5))
                shade = (1.0 - 0.14 * 0.62) + 0.14 * t_h
                if nz < -0.42:
                    shade *= 0.82
                elif nz > 0.52:
                    shade *= 1.06
                ao = max(0.50, min(1.0, lin[3] * (0.86 + 0.14 * t_h)))
                vert_cols[v] = (
                    max(0.012, min(1.0, lin[0] * shade)),
                    max(0.012, min(1.0, lin[1] * shade)),
                    max(0.012, min(1.0, lin[2] * shade)),
                    ao,
                )
            for f in bough_faces:
                for loop in f.loops:
                    loop[self.color_layer] = vert_cols[loop.vert]
            return

        # 4 istasyonlu sarkik dal profili (kok -> orta genisleme -> tirtikli sarkik uc)
        stations = [
            (0.00, 0.16 * width, 0.045, 0.00),
            (0.34, 0.56 * width, 0.115, -0.22 * droop),
            (0.72, 0.44 * width, 0.090, -0.64 * droop),
            (1.00, 0.05 * width, 0.022, -1.00 * droop),
        ]
        ring_verts = []
        prof_angles = [
            (0.0, 1.0),
            (0.62, 0.72),
            (1.00, 0.12),
            (0.55, -0.58),
            (0.0, -0.82),
            (-0.55, -0.58),
            (-1.00, 0.12),
            (-0.62, 0.72),
        ]
        for st_i, (t, half_w, half_h, dz) in enumerate(stations):
            c_pt = orig + d2 * (t * length) + Vector((0.0, 0.0, dz))
            rv = []
            for p_i, (lat_k, up_k) in enumerate(prof_angles):
                jag = 1.0 + 0.14 * math.sin(seed_val * 5.7 + st_i * 2.3 + p_i * 1.9)
                pt = (
                    c_pt
                    + perp * (lat_k * half_w * jag)
                    + Vector((0.0, 0.0, up_k * half_h * jag))
                )
                rv.append(self.bm.verts.new(pt))
            ring_verts.append(rv)

        n_prof = len(prof_angles)
        for i in range(len(ring_verts) - 1):
            r0 = ring_verts[i]
            r1 = ring_verts[i + 1]
            for s in range(n_prof):
                sn = (s + 1) % n_prof
                try:
                    f = self.bm.faces.new((r0[s], r0[sn], r1[sn], r1[s]))
                    _paint_bough_face(f, i)
                except ValueError:
                    pass

        for end_i, flip in ((0, True), (-1, False)):
            ring = ring_verts[end_i]
            ctr = sum((v.co for v in ring), Vector((0.0, 0.0, 0.0))) / len(ring)
            cv = self.bm.verts.new(ctr)
            for s in range(n_prof):
                sn = (s + 1) % n_prof
                try:
                    f = (
                        self.bm.faces.new((ring[sn], ring[s], cv))
                        if flip
                        else self.bm.faces.new((ring[s], ring[sn], cv))
                    )
                    bmesh.ops.recalc_face_normals(self.bm, faces=[f])
                    c_end = PAL["snow_shade"] if (snowy and not flip) else PAL["pine_mid"]
                    self.paint_faces([f], c_end, z_ao_origin=-1.2, z_ao_span=5.5)
                except ValueError:
                    pass

    def add_organic_foliage_lobe(
        self,
        center,
        radii,
        seed_val,
        palette_quad,
        subdivs=3,
        smooth_shared=False,
    ):
        """
        Asla kup veya tek top olmayan, alt-bolumlenmis yassi bulutsu organik mese yaprak obegi.
        palette_quad = (deep_shadow, mid_leaf, sun_leaf, warm_tip)
        """
        prev_faces = set(self.bm.faces)
        ret = bmesh.ops.create_icosphere(self.bm, subdivisions=subdivs, radius=1.0)
        new_verts = ret["verts"]
        c_vec = Vector(center)

        for v in new_verts:
            d = v.co.normalized()
            # Cok-oktavli dogal yaprak demeti girinti-cikintisi
            w1 = math.sin(d.x * 5.1 + seed_val * 1.7) * math.cos(d.y * 4.7 - seed_val * 1.3)
            w2 = math.sin((d.x - d.y) * 8.3 + d.z * 6.1 + seed_val * 2.9)
            amp1, amp2 = (0.12, 0.06) if subdivs <= 1 else (0.19, 0.11)
            disp = 1.0 + amp1 * w1 + amp2 * w2
            # Alt yuzeyi semsiye gibi icbukey/duz yap ki alttaki kalin mese dallari net gorunsun!
            z_squash = 0.54 if d.z < -0.10 else 1.06
            v.co = c_vec + Vector(
                (
                    d.x * radii[0] * disp,
                    d.y * radii[1] * disp,
                    d.z * radii[2] * disp * z_squash,
                )
            )

        new_faces = [f for f in self.bm.faces if f not in prev_faces and f.is_valid]
        bmesh.ops.recalc_face_normals(self.bm, faces=new_faces)

        if smooth_shared:
            for f in new_faces:
                f.smooth = True
            self.bm.normal_update()
            vert_cols = {}
            for v in new_verts:
                nz = v.normal.z
                tonal = math.sin(v.co.x * 5.8 + v.co.y * 5.1 + seed_val)
                if nz < -0.20:
                    col_u8 = palette_quad[0]
                elif nz < 0.30:
                    col_u8 = palette_quad[1]
                elif tonal > 0.35 and nz > 0.52:
                    col_u8 = palette_quad[3]
                else:
                    col_u8 = palette_quad[2]
                lin = srgb_to_linear_rgba(col_u8)
                t_h = max(0.0, min(1.0, (v.co.z - 0.2) / 4.2))
                shade = (1.0 - 0.18 * 0.62) + 0.18 * t_h
                if nz < -0.42:
                    shade *= 0.82
                elif nz > 0.52:
                    shade *= 1.06
                ao = max(0.50, min(1.0, lin[3] * (0.86 + 0.14 * t_h)))
                vert_cols[v] = (
                    max(0.012, min(1.0, lin[0] * shade)),
                    max(0.012, min(1.0, lin[1] * shade)),
                    max(0.012, min(1.0, lin[2] * shade)),
                    ao,
                )
            for f in new_faces:
                for loop in f.loops:
                    loop[self.color_layer] = vert_cols[loop.vert]
            return

        for f in new_faces:
            fc = f.calc_center_median()
            nz = f.normal.z
            tonal = math.sin(fc.x * 5.8 + fc.y * 5.1 + seed_val)
            if nz < -0.20:
                col = palette_quad[0]
            elif nz < 0.30:
                col = palette_quad[1]
            elif tonal > 0.35 and nz > 0.52:
                col = palette_quad[3]
            else:
                col = palette_quad[2]
            self.paint_faces([f], col, z_ao_origin=0.2, z_ao_span=4.2, z_ao_strength=0.18)

    def finalize_object(self, mat, smooth_shading=False, preserve_face_smooth=False):
        bmesh.ops.triangulate(self.bm, faces=self.bm.faces[:])
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces[:])

        mesh = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(mesh)
        self.bm.free()

        if not preserve_face_smooth:
            for p in mesh.polygons:
                p.use_smooth = smooth_shading

        obj = bpy.data.objects.new(self.name, mesh)
        obj.location = Vector((0.0, 0.0, 0.0))
        obj.data.materials.append(mat)
        bpy.context.collection.objects.link(obj)
        return obj


# ==============================================================================
# 13 PARCALI GERCEK NORDIK KUTUK-TAS MODULER YAPI SETI
# ==============================================================================


def build_mod_log_beam_2m(mat):
    """Ø 0.26m, 2.08m uclu damarli Nordik kutuk kiris; X=±0.90m eyer centikli, demir kusakli."""
    mb = SculptedMeshBuilder("Mod_Log_Beam_2m")
    mb.add_notched_log(
        length=2.08,
        radius=0.13,
        notch_positions=(-0.90, 0.90),
        along_axis="X",
        center_offset=(0.0, 0.0, 0.13),
        segs=16,
    )
    for sx in (-0.90, 0.90):
        mb.add_box((sx, 0.0, 0.246), (0.024, 0.024, 0.018), PAL["wood_peg"], bevel_amt=0.004)
        mb.add_box((sx * 0.62, 0.0, 0.13), (0.018, 0.132, 0.132), PAL["iron_hardware"], bevel_amt=0.003)
    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_log_pillar_3m(mat):
    """Ø 0.27m, 3.00m dikey Nordik kutuk sutun; alt/orta/ust eyer centikli + demir taban papucu."""
    mb = SculptedMeshBuilder("Mod_Log_Pillar_3m")
    mb.add_notched_log(
        length=3.00,
        radius=0.135,
        notch_positions=(-1.28, 0.42, 1.32),
        along_axis="Z",
        center_offset=(0.0, 0.0, 0.0),
        segs=16,
    )
    mb.add_box((0.0, 0.0, 0.045), (0.148, 0.148, 0.045), PAL["stone_dark"], bevel_amt=0.008)
    mb.add_box((0.0, 0.0, 0.105), (0.142, 0.142, 0.018), PAL["iron_hardware"], bevel_amt=0.004)
    mb.add_box((0.0, 0.0, 2.94), (0.140, 0.140, 0.018), PAL["iron_hardware"], bevel_amt=0.004)
    return mb.finalize_object(mat, smooth_shading=False)


def _add_nordic_log_wall_span(mb, x0, x1, z0, z1, row_count, include_cross_tails=False):
    """Verilen X-Z bolgesine yosun harc derzli (chinked) yuvarlatilmis Nordik kutuk duvar siralari orer."""
    cx = 0.5 * (x0 + x1)
    hx = 0.5 * (x1 - x0)
    if hx <= 0.02 or z1 <= z0 + 0.02:
        return
    step_z = (z1 - z0) / row_count
    tones = (
        PAL["log_peeled_warm"],
        PAL["log_peeled_mid"],
        PAL["plank_warm"],
        PAL["log_peeled_shade"],
    )
    for r in range(row_count):
        cz = z0 + (r + 0.5) * step_z
        # Derz dolgusu (chinking)
        if r > 0:
            mb.add_box(
                (cx, 0.0, z0 + r * step_z),
                (hx * 0.99, 0.044, step_z * 0.12),
                PAL["chink_moss_mortar"],
                bevel_amt=0.002,
            )
        # Pahli dolgun Nordik duvar kutugu (0.128m kalinlik)
        mb.add_box(
            (cx, 0.0, cz),
            (hx, 0.062, step_z * 0.46),
            tones[r % len(tones)],
            bevel_amt=0.018,
            grain_freq=10.5,
        )
        if include_cross_tails and (r % 2 == 0):
            for ex in (-0.955, 0.955):
                mb.add_box(
                    (ex, 0.0, cz),
                    (0.042, 0.088, step_z * 0.42),
                    PAL["log_endgrain_outer"],
                    bevel_amt=0.010,
                )


def build_mod_plank_wall_2x2m(mat):
    """2.0m x 2.0m gecmeli Nordik kutuk duvar + kose karkas dikmeleri + ust/alt yastik kirisleri."""
    mb = SculptedMeshBuilder("Mod_Plank_Wall_2x2m")
    mb.add_box((0.0, 0.0, 0.045), (1.00, 0.066, 0.045), PAL["stud_frame"], bevel_amt=0.008, grain_freq=8.0)
    mb.add_box((0.0, 0.0, 1.955), (1.00, 0.066, 0.045), PAL["stud_frame"], bevel_amt=0.008, grain_freq=8.0)
    mb.add_box((-0.945, 0.0, 1.00), (0.055, 0.068, 0.92), PAL["stud_frame"], bevel_amt=0.008, grain_freq=8.0)
    mb.add_box((0.945, 0.0, 1.00), (0.055, 0.068, 0.92), PAL["stud_frame"], bevel_amt=0.008, grain_freq=8.0)
    mb.add_box((0.0, 0.0, 1.00), (0.042, 0.058, 0.92), PAL["stud_frame"], bevel_amt=0.006)
    _add_nordic_log_wall_span(mb, -0.89, -0.042, 0.09, 1.91, 8, include_cross_tails=True)
    _add_nordic_log_wall_span(mb, 0.042, 0.89, 0.09, 1.91, 8, include_cross_tails=False)
    for sx in (-0.945, 0.0, 0.945):
        for sz in (0.36, 1.00, 1.64):
            mb.add_box((sx, -0.070, sz), (0.014, 0.006, 0.014), PAL["iron_hardware"], bevel_amt=0.002)
            mb.add_box((sx, 0.070, sz), (0.014, 0.006, 0.014), PAL["iron_hardware"], bevel_amt=0.002)
    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_plank_doorway_2x2m(mat):
    """2.0m x 2.0m oymali lento kemerli, demir menteseli gecilebilir Nordik kutuk kapili duvar."""
    mb = SculptedMeshBuilder("Mod_Plank_Doorway_2x2m")
    mb.add_box((-0.945, 0.0, 1.00), (0.055, 0.068, 1.00), PAL["stud_frame"], bevel_amt=0.008)
    mb.add_box((0.945, 0.0, 1.00), (0.055, 0.068, 1.00), PAL["stud_frame"], bevel_amt=0.008)
    mb.add_box((0.0, 0.0, 1.955), (0.89, 0.068, 0.045), PAL["stud_frame"], bevel_amt=0.008)
    mb.add_box((-0.72, 0.0, 0.045), (0.22, 0.066, 0.045), PAL["stud_frame"], bevel_amt=0.006)
    mb.add_box((0.72, 0.0, 0.045), (0.22, 0.066, 0.045), PAL["stud_frame"], bevel_amt=0.006)
    mb.add_box((0.0, 0.0, 0.022), (0.50, 0.076, 0.022), PAL["stone_light"], bevel_amt=0.006)
    # Agir kapi sove dikmeleri + oymali ust lento + Nordik kemer takozlari
    mb.add_box((-0.49, 0.0, 0.89), (0.054, 0.074, 0.85), PAL["stud_frame"], bevel_amt=0.008)
    mb.add_box((0.49, 0.0, 0.89), (0.054, 0.074, 0.85), PAL["stud_frame"], bevel_amt=0.008)
    mb.add_box((0.0, 0.0, 1.76), (0.56, 0.080, 0.060), PAL["sill_weathered"], bevel_amt=0.009)
    for sign in (-1.0, 1.0):
        mb.add_box(
            (sign * 0.37, 0.0, 1.63),
            (0.12, 0.060, 0.040),
            PAL["plank_dark"],
            rot_euler=(0.0, -sign * 0.62, 0.0),
            bevel_amt=0.005,
        )
        # Dovme demir sove menteseleri
        for hz in (0.48, 1.38):
            mb.add_box(
                (sign * 0.50, -0.074, hz), (0.048, 0.008, 0.018), PAL["iron_hardware"], bevel_amt=0.002
            )
            mb.add_box(
                (sign * 0.50, 0.074, hz), (0.048, 0.008, 0.018), PAL["iron_hardware"], bevel_amt=0.002
            )
    _add_nordic_log_wall_span(mb, -0.89, -0.545, 0.09, 1.91, 8, include_cross_tails=False)
    _add_nordic_log_wall_span(mb, 0.545, 0.89, 0.09, 1.91, 8, include_cross_tails=False)
    _add_nordic_log_wall_span(mb, -0.545, 0.545, 1.82, 1.91, 1, include_cross_tails=False)
    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_plank_window_2x2m(mat):
    """2.0m x 2.0m damlalikli denizlikli, artili kayitli ve cift ahsap KEPENKLI Nordik pencere duvari."""
    mb = SculptedMeshBuilder("Mod_Plank_Window_2x2m")
    mb.add_box((0.0, 0.0, 0.045), (1.00, 0.066, 0.045), PAL["stud_frame"], bevel_amt=0.008)
    mb.add_box((0.0, 0.0, 1.955), (1.00, 0.066, 0.045), PAL["stud_frame"], bevel_amt=0.008)
    mb.add_box((-0.945, 0.0, 1.00), (0.055, 0.068, 0.92), PAL["stud_frame"], bevel_amt=0.008)
    mb.add_box((0.945, 0.0, 1.00), (0.055, 0.068, 0.92), PAL["stud_frame"], bevel_amt=0.008)
    # Pencere kasasi (X in [-0.46..+0.46], Z in [0.84..1.56]) + disari tasan denizlik & yagmurluk lento
    mb.add_box((-0.49, 0.0, 1.20), (0.046, 0.072, 0.40), PAL["stud_frame"], bevel_amt=0.006)
    mb.add_box((0.49, 0.0, 1.20), (0.046, 0.072, 0.40), PAL["stud_frame"], bevel_amt=0.006)
    mb.add_box((0.0, 0.0, 0.81), (0.58, 0.095, 0.042), PAL["sill_weathered"], bevel_amt=0.007)
    mb.add_box((0.0, 0.0, 1.60), (0.58, 0.088, 0.046), PAL["sill_weathered"], bevel_amt=0.007)
    # Artili (+) ahsap pencere kayitlari (mullion + transom)
    mb.add_box((0.0, 0.0, 1.20), (0.022, 0.040, 0.35), PAL["plank_dark"], bevel_amt=0.004)
    mb.add_box((0.0, 0.0, 1.22), (0.44, 0.036, 0.020), PAL["plank_dark"], bevel_amt=0.004)

    # Iki yuzde de (on ve arka simetrik) disa acilmis Z-kusakli ahsap KEPENKLER + demir menteseler!
    for face_sign in (-1.0, 1.0):
        for side_sign in (-1.0, 1.0):
            sh_x = side_sign * 0.685
            sh_y = face_sign * 0.082
            yaw_ang = face_sign * side_sign * 0.24
            mb.add_box(
                (sh_x, sh_y, 1.20),
                (0.165, 0.018, 0.355),
                PAL["shutter_pine"],
                rot_euler=(0.0, 0.0, yaw_ang),
                bevel_amt=0.004,
                grain_freq=14.0,
            )
            for bar_z in (0.94, 1.46):
                mb.add_box(
                    (sh_x, sh_y + face_sign * 0.014, bar_z),
                    (0.158, 0.010, 0.028),
                    PAL["shutter_trim"],
                    rot_euler=(0.0, 0.0, yaw_ang),
                    bevel_amt=0.003,
                )
                mb.add_box(
                    (side_sign * 0.53, sh_y + face_sign * 0.016, bar_z),
                    (0.045, 0.008, 0.014),
                    PAL["iron_hardware"],
                    bevel_amt=0.002,
                )

    _add_nordic_log_wall_span(mb, -0.89, 0.89, 0.09, 0.77, 4)
    _add_nordic_log_wall_span(mb, -0.89, -0.535, 0.77, 1.91, 5)
    _add_nordic_log_wall_span(mb, 0.535, 0.89, 0.77, 1.91, 5)
    _add_nordic_log_wall_span(mb, -0.535, 0.535, 1.645, 1.91, 2)
    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_plank_floor_2x2m(mat):
    """2.0m x 2.0m, 0.10m kalinlikta 4 alt kirisli yontma Nordik doseme tahtasi."""
    mb = SculptedMeshBuilder("Mod_Plank_Floor_2x2m")
    for jx in (-0.90, -0.30, 0.30, 0.90):
        mb.add_box((jx, 0.0, 0.030), (0.052, 0.99, 0.030), PAL["joist_heavy"], bevel_amt=0.005)
    mb.add_box((0.0, -0.955, 0.030), (0.86, 0.042, 0.030), PAL["joist_heavy"], bevel_amt=0.004)
    mb.add_box((0.0, 0.955, 0.030), (0.86, 0.042, 0.030), PAL["joist_heavy"], bevel_amt=0.004)
    tones = (PAL["plank_light"], PAL["plank_warm"], PAL["plank_mid"], PAL["plank_dark"])
    board_count = 9
    step_y = 2.0 / board_count
    for b in range(board_count):
        cy = -1.0 + (b + 0.5) * step_y
        mb.add_box(
            (0.0, cy, 0.078),
            (0.996, step_y * 0.476, 0.022),
            tones[b % len(tones)],
            bevel_amt=0.005,
            grain_freq=12.0,
        )
        for nx in (-0.88, 0.0, 0.88):
            mb.add_box((nx, cy, 0.101), (0.011, 0.011, 0.003), PAL["iron_hardware"], bevel_amt=0.001)
    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_stone_foundation_2x2m(mat):
    """
    2.0m x 2.0m harcli Nordik tarla tasi subasman:
    Ust doseme kotu Z = +0.32m korunur; egimli arazide asla havada kalmamasi icin
    Z = -0.85m derinlige inen hafif sevli (battered) 4 katmanli kagir tas etek icerir!
    """
    mb = SculptedMeshBuilder("Mod_Stone_Foundation_2x2m")
    # Derin harc cekirdek govdesi (Z in [-0.84 .. +0.295], hafif taban genislemesi)
    mb.add_box((0.0, 0.0, -0.52), (0.98, 0.98, 0.32), PAL["stone_deep_skirt"], bevel_amt=0.014)
    mb.add_box((0.0, 0.0, -0.02), (0.95, 0.95, 0.31), PAL["mortar_bed"], bevel_amt=0.012)

    stone_tones = (
        PAL["stone_warm"],
        PAL["stone_cool"],
        PAL["stone_light"],
        PAL["stone_dark"],
        PAL["stone_moss_tint"],
    )
    seed = 1
    # 4 kose masif baglama taslari (quoins) - 4 dusey kademe (-0.70m -> +0.22m)
    quoin_zs = [(-0.68, 1.04, 0.14), (-0.38, 1.02, 0.13), (-0.08, 1.00, 0.12), (0.20, 0.98, 0.105)]
    for kx in (-0.80, 0.80):
        for ky in (-0.80, 0.80):
            for qz, flare, hz in quoin_zs:
                tone = PAL["stone_moss_tint"] if qz < -0.50 else stone_tones[seed % len(stone_tones)]
                mb.add_irregular_stone((kx * flare, ky * flare, qz), (0.22, 0.22, hz), tone, seed)
                seed += 1

    # 4 yan cephe boyunca sasirtmali (running bond) kagir tarla taslari
    courses = [
        (-0.68, 0.88, 0.135, (-0.44, 0.0, 0.44)),
        (-0.38, 0.86, 0.125, (-0.24, 0.24)),
        (-0.08, 0.84, 0.115, (-0.42, 0.0, 0.42)),
        (0.20, 0.83, 0.100, (-0.22, 0.22)),
    ]
    for cz, wall_out, hz, offsets in courses:
        for eo in offsets:
            for side_sign in (-1.0, 1.0):
                t_a = PAL["stone_moss_tint"] if (cz < -0.5 and seed % 2 == 0) else stone_tones[seed % 5]
                mb.add_irregular_stone((eo, side_sign * wall_out, cz), (0.21, 0.175, hz), t_a, seed)
                seed += 1
                t_b = PAL["stone_moss_tint"] if (cz < -0.5 and seed % 2 == 1) else stone_tones[seed % 5]
                mb.add_irregular_stone((side_sign * wall_out, eo, cz), (0.175, 0.21, hz), t_b, seed)
                seed += 1

    # Ust yuzey yontma kayrak doseme taslari + kordon silmesi (Z ust = +0.32m)
    for gx in (-0.48, 0.0, 0.48):
        for gy in (-0.48, 0.0, 0.48):
            mb.add_box(
                (gx, gy, 0.296),
                (0.235, 0.235, 0.024),
                stone_tones[seed % len(stone_tones)],
                rot_euler=(0.0, 0.0, ((seed % 5) - 2) * 0.02),
                bevel_amt=0.010,
                grain_freq=5.0,
            )
            seed += 1
    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_stone_arch_pillar_3m(mat):
    """
    3.00m yuksekliginde yontma tas sutun (Z in [0.00..3.00m]):
    Ustte Z = 2.86..3.00m arasinda TAM DUZ tasiyici abakus tablasi bulunur; kavisli kemer konsollari
    Z = 2.38..2.84m altinda kalir, boylece Z = 3.00m ustundeki kutuk kirislerin icinden ASLA gecmez!
    """
    mb = SculptedMeshBuilder("Mod_Stone_Arch_Pillar_3m")
    # Kademeli pahli tas kaide (Z in [0.00..0.40])
    mb.add_box((0.0, 0.0, 0.09), (0.24, 0.24, 0.09), PAL["stone_dark"], bevel_amt=0.016)
    mb.add_box((0.0, 0.0, 0.24), (0.205, 0.205, 0.075), PAL["arch_carved"], bevel_amt=0.014)
    mb.add_box((0.0, 0.0, 0.35), (0.175, 0.175, 0.040), PAL["stone_light"], bevel_amt=0.010)

    # Derzli yontma tas sutun tamburlari (Z in [0.39..2.34])
    tones = (PAL["arch_carved"], PAL["stone_light"], PAL["stone_warm"], PAL["stone_cool"])
    mb.add_box((0.0, 0.0, 1.36), (0.140, 0.140, 0.98), PAL["mortar_bed"], bevel_amt=0.006)
    drum_count = 7
    z_start = 0.40
    dz = 1.94 / drum_count
    for d in range(drum_count):
        cz = z_start + (d + 0.5) * dz
        yaw = 0.025 if (d % 2 == 1) else -0.025
        mb.add_irregular_stone(
            (0.0, 0.0, cz), (0.162, 0.162, dz * 0.44), tones[d % len(tones)], d + 17, (0.0, 0.0, yaw)
        )

    # Boyun bilezigi + kavisli kemer konsollari (Z <= 2.84m) + DUZ ABAKUS TABLASI (Z=2.86..3.00m)
    mb.add_box((0.0, 0.0, 2.40), (0.195, 0.195, 0.055), PAL["arch_keystone"], bevel_amt=0.012)
    mb.add_box((0.0, 0.0, 2.64), (0.185, 0.175, 0.19), PAL["arch_carved"], bevel_amt=0.012)
    for sign in (-1.0, 1.0):
        mb.add_box(
            (sign * 0.21, 0.0, 2.56),
            (0.10, 0.145, 0.095),
            PAL["stone_light"],
            rot_euler=(0.0, sign * 0.42, 0.0),
            bevel_amt=0.010,
        )
        mb.add_box(
            (sign * 0.30, 0.0, 2.74),
            (0.11, 0.145, 0.085),
            PAL["arch_keystone"],
            rot_euler=(0.0, sign * 0.62, 0.0),
            bevel_amt=0.010,
        )
    # Tam duz ust yastik abakus (Z ust = 3.00m -> ustteki kiris %100 duz oturur!)
    mb.add_box((0.0, 0.0, 2.925), (0.36, 0.21, 0.075), PAL["arch_keystone"], bevel_amt=0.012)
    return mb.finalize_object(mat, smooth_shading=False)


def _build_pitched_shingle_roof(name, rise_height, shingle_rows, mat):
    """2.0m x 2.0m mertekli, alin tahtali ve kalin kademeli kiremit/zona kapli cati."""
    mb = SculptedMeshBuilder(name)
    slope_angle = math.atan2(rise_height, 2.0)
    slope_len = math.sqrt(4.0 + rise_height * rise_height)
    tang = Vector((0.0, -2.0 / slope_len, rise_height / slope_len))
    up_n = Vector((0.0, rise_height / slope_len, 2.0 / slope_len))
    rot_x = -slope_angle

    total_s = slope_len + 0.12
    mid_s = 0.5 * (slope_len - 0.06)
    eave_origin = Vector((0.0, 1.0, 0.0))

    # 4 kalin alt mertek + yan bargeboard alin kirisleri
    for rx in (-0.94, -0.32, 0.32, 0.94):
        c_raf = eave_origin + tang * mid_s + up_n * 0.044 + Vector((rx, 0.0, 0.0))
        mb.add_box(
            c_raf,
            (0.048, total_s * 0.5, 0.042),
            PAL["rafter_timber"],
            rot_euler=(rot_x, 0.0, 0.0),
            bevel_amt=0.006,
            grain_freq=10.0,
        )

    # Sacak alin tahtasi (fascia)
    c_fascia = eave_origin + tang * (-0.065) + up_n * 0.068
    mb.add_box(
        c_fascia, (0.998, 0.022, 0.058), PAL["stud_frame"], rot_euler=(rot_x, 0.0, 0.0), bevel_amt=0.005
    )

    # Alt kaplama tahtasi (sheathing)
    c_deck = eave_origin + tang * mid_s + up_n * 0.095
    mb.add_box(
        c_deck,
        (0.998, total_s * 0.5, 0.018),
        PAL["plank_dark"],
        rot_euler=(rot_x, 0.0, 0.0),
        bevel_amt=0.004,
    )

    # Kalin kademeli, pahli Nordik cam zonasi & arduvaz kiremit siralari
    shingle_tones = (
        PAL["shingle_dark"],
        PAL["shingle_mid"],
        PAL["shingle_warm"],
        PAL["shingle_slate"],
        PAL["shingle_moss"],
    )
    step_s = total_s / shingle_rows
    tiles_per_row = 7
    tile_w = 2.0 / tiles_per_row
    for r in range(shingle_rows):
        s_pos = -0.06 + (r + 0.5) * step_s
        overlap_tilt = rot_x + 0.068
        stagger = (tile_w * 0.24) if (r % 2 == 1) else -(tile_w * 0.24)
        for t_idx in range(tiles_per_row):
            raw_x = -1.0 + (t_idx + 0.5) * tile_w + stagger
            x0 = max(-1.0, raw_x - tile_w * 0.49)
            x1 = min(1.0, raw_x + tile_w * 0.49)
            if x1 - x0 < 0.06:
                continue
            cx = 0.5 * (x0 + x1)
            hx = 0.5 * (x1 - x0) - 0.003
            z_jitter = ((r * 5 + t_idx) % 3 - 1) * 0.003
            c_sh = eave_origin + tang * s_pos + up_n * (0.128 + z_jitter) + Vector((cx, 0.0, 0.0))
            tone_idx = (r * 3 + t_idx) % 4
            if r == 0 and t_idx % 3 == 1:
                tone_idx = 4
            mb.add_box(
                c_sh,
                (hx, step_s * 0.58, 0.022),
                shingle_tones[tone_idx],
                rot_euler=(overlap_tilt, 0.0, 0.0),
                bevel_amt=0.005,
                grain_freq=14.0,
            )

    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_roof_rafter_26deg_2x2m(mat):
    return _build_pitched_shingle_roof("Mod_Roof_Rafter_26deg_2x2m", rise_height=1.0, shingle_rows=8, mat=mat)


def build_mod_roof_rafter_45deg_2x2m(mat):
    return _build_pitched_shingle_roof(
        "Mod_Roof_Rafter_45deg_2x2m", rise_height=2.0, shingle_rows=11, mat=mat
    )


def build_mod_roof_ridge_2m(mat):
    """2.04m ters-V (Λ) eyer kanatli kademeli mahya kapagi + oymali Nordik mahya omurgasi."""
    mb = SculptedMeshBuilder("Mod_Roof_Ridge_2m")
    mb.add_box((0.0, 0.0, 0.125), (1.02, 0.068, 0.052), PAL["rafter_timber"], bevel_amt=0.006)
    for sign in (-1.0, 1.0):
        mb.add_box(
            (0.0, sign * 0.168, 0.082),
            (1.015, 0.220, 0.024),
            PAL["plank_dark"],
            rot_euler=(-sign * 0.62, 0.0, 0.0),
            bevel_amt=0.005,
        )
        for k in range(7):
            cx = -1.0 + (k + 0.5) * (2.0 / 7.0)
            tone = PAL["shingle_mid"] if (k % 2 == 0) else PAL["shingle_dark"]
            mb.add_box(
                (cx, sign * 0.178, 0.114),
                (0.14, 0.225, 0.022),
                tone,
                rot_euler=(-sign * 0.62, 0.0, 0.0),
                bevel_amt=0.005,
            )
    mb.add_box(
        (0.0, 0.0, 0.210),
        (1.03, 0.055, 0.055),
        PAL["log_peeled_warm"],
        rot_euler=(0.785, 0.0, 0.0),
        bevel_amt=0.009,
        grain_freq=10.0,
    )
    for ex in (-0.98, 0.98):
        for r_sign in (-1.0, 1.0):
            mb.add_box(
                (ex, 0.0, 0.255),
                (0.030, 0.145, 0.030),
                PAL["sill_weathered"],
                rot_euler=(r_sign * 0.68, 0.0, 0.0),
                bevel_amt=0.005,
            )
    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_diagonal_brace_2m(mat):
    """45° capraz gogusleme payandasi: Unity (-0.70, 0.0, 0) -> (+0.70, +1.40, 0)."""
    mb = SculptedMeshBuilder("Mod_Diagonal_Brace_2m")
    mb.add_box(
        (0.0, 0.0, 0.71),
        (0.090, 0.082, 0.98),
        PAL["log_peeled_mid"],
        rot_euler=(0.0, -0.785398, 0.0),
        bevel_amt=0.012,
        grain_freq=10.0,
    )
    mb.add_box((0.68, 0.0, 0.06), (0.12, 0.094, 0.06), PAL["stud_frame"], bevel_amt=0.006)
    mb.add_box((-0.68, 0.0, 1.36), (0.12, 0.094, 0.06), PAL["stud_frame"], bevel_amt=0.006)
    for bx, bz in ((0.52, 0.19), (-0.52, 1.23)):
        mb.add_box(
            (bx, 0.0, bz),
            (0.034, 0.096, 0.034),
            PAL["iron_hardware"],
            rot_euler=(0.0, -0.785398, 0.0),
            bevel_amt=0.004,
        )
    mb.add_box(
        (0.0, 0.0, 0.71),
        (0.100, 0.090, 0.055),
        PAL["wood_peg"],
        rot_euler=(0.0, -0.785398, 0.0),
        bevel_amt=0.005,
    )
    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_gable_wall_45deg_2m(mat):
    """
    45° Ucgen Cati Alinlik Duvari (ModularBuildPieceKind.GableWall45Deg2m):
    Unity yerel uzayda (yaw=0°):
      - Taban X in [-1.00 .. +1.00], Y = 0.00m, Z in [-0.07 .. +0.07] (on/arka %100 simetrik!)
      - Sol sacak kosesi (X_u = -1.00 -> Blender X_b = +1.00): Y_u = 0.05m
      - Sag mahya dikmesi (X_u = +1.00 -> Blender X_b = -1.00): Y_u = 2.05m
    Iki adet GableWall45Deg2m (yaw=0° ve yaw=180°) 4m'lik bina cephesinde %100 sizdirmaz Λ alinlik kurar!
    """
    mb = SculptedMeshBuilder("Mod_Gable_Wall_45deg_2m")
    # Alt taban yastik kirisi (X_b in [-1.00..+1.00], Z_b in [0.00..0.08])
    mb.add_box((0.0, 0.0, 0.042), (1.00, 0.066, 0.042), PAL["stud_frame"], bevel_amt=0.007, grain_freq=8.0)
    # Yuksek mahya ucu dikey kral dikmesi (Blender X_b in [-1.00..-0.90] -> Unity X_u in [+0.90..+1.00])
    mb.add_box((-0.95, 0.0, 1.02), (0.050, 0.068, 0.98), PAL["stud_frame"], bevel_amt=0.007, grain_freq=8.0)

    # 8 sira yamuk kesimli Nordik kutuk/kalas + derz dolgusu (45° egim cizgisini ASLA asmaz!)
    tones = (
        PAL["log_peeled_warm"],
        PAL["log_peeled_mid"],
        PAL["plank_warm"],
        PAL["log_peeled_shade"],
    )
    rows = 9
    z_start = 0.084
    z_end = 1.985
    dz = (z_end - z_start) / rows
    for r in range(rows):
        zb = z_start + r * dz + 0.004
        zt = z_start + (r + 1) * dz - 0.004
        # 45° ust sinir: z = 1.04 - x_b  =>  x_b_max(z) = 1.00 - z
        xb_max_bot = min(0.95, 1.00 - zb)
        xb_max_top = min(0.95, 1.00 - zt)
        xb_min = -0.90
        if xb_max_top <= xb_min + 0.02:
            continue
        if r > 0:
            mb.add_trapezoid_prism_x(
                xb_min,
                xb_max_bot,
                xb_min,
                max(xb_min + 0.02, xb_max_bot - 0.015),
                zb - 0.010,
                zb + 0.010,
                0.0,
                0.044,
                PAL["chink_moss_mortar"],
                bevel_amt=0.001,
            )
        mb.add_trapezoid_prism_x(
            xb_min,
            xb_max_bot,
            xb_min,
            xb_max_top,
            zb,
            zt,
            0.0,
            0.060,
            tones[r % len(tones)],
            bevel_amt=0.008,
        )

    # 45° egimli kalin alinlik mertegi (bargeboard rafter): Blender (+1.00, 0, 0.04) -> (-1.00, 0, 2.04)
    diag_half_len = math.sqrt(2.0 * 2.0 + 2.0 * 2.0) * 0.5
    mb.add_box(
        (0.0, 0.0, 1.045),
        (0.052, 0.072, diag_half_len),
        PAL["stud_frame"],
        rot_euler=(0.0, -0.785398, 0.0),
        bevel_amt=0.008,
        grain_freq=9.0,
    )
    # Nordik kalkan havalandirma rozeti & demir kenet plakasi
    mb.add_box(
        (-0.62, 0.0, 0.72),
        (0.095, 0.072, 0.095),
        PAL["sill_weathered"],
        rot_euler=(0.0, 0.785398, 0.0),
        bevel_amt=0.006,
    )
    mb.add_box((-0.94, 0.0, 1.88), (0.045, 0.074, 0.024), PAL["iron_hardware"], bevel_amt=0.003)
    return mb.finalize_object(mat, smooth_shading=False)


# ==============================================================================
# 4 ORGANIK 3B FLORA MODELI (CAM TACI, KARLI CAM, MESE TACI, KOK MAHMUZU)
# ==============================================================================


def _build_pine_canopy_common(name, snowy, mat):
    """
    Cok katmanli sarkik igne yaprak salkimli & dal iskeletli + konik tepeli gercek 3B Nordik cam taci.
    Pivot (0,0,0) = CrownBaseY; alt sarkik dallar Z = -1.25m'ye kadar iner, konik tepe Z = +4.65m'ye yukselir.
    """
    mb = SculptedMeshBuilder(name)
    # Ust incelen cam govde lideri (leader trunk)
    mb.add_tapered_tube(
        points=[(0.0, 0.0, -1.35), (0.02, -0.02, 1.30), (0.0, 0.0, 4.25)],
        radii=[0.24, 0.14, 0.04],
        cols_u8=[PAL["bark_dark"], PAL["bark_ridge"]],
        segs=5,
    )

    # 6 katmanli (whorl) altin-aci kaydirmali sarkik cam dallari ve igne yaprak salkimlari
    whorls = [
        (-0.85, 5, 2.38, 1.24, 0.44),
        (-0.02, 5, 2.04, 1.12, 0.38),
        (0.82, 4, 1.68, 0.96, 0.32),
        (1.66, 4, 1.30, 0.80, 0.26),
        (2.48, 3, 0.94, 0.62, 0.20),
        (3.25, 3, 0.60, 0.44, 0.14),
    ]
    seed = 1
    for w_idx, (wz, count, b_len, b_width, droop) in enumerate(whorls):
        base_ang = w_idx * 2.39996  # Altin aci -> robotik hizalanmayi onler
        for b_i in range(count):
            ang = base_ang + (2.0 * math.pi * b_i) / count + 0.09 * math.sin(seed * 3.1)
            dx = math.cos(ang)
            dy = math.sin(ang)
            len_var = b_len * (0.90 + 0.18 * ((seed * 37 % 11) / 11.0))

            # Alt 2 katmanda gorunur ahsap ic dal iskeleti
            if w_idx <= 1:
                p0 = (dx * 0.08, dy * 0.08, wz + 0.04)
                p2 = (dx * len_var * 0.78, dy * len_var * 0.78, wz - droop * 0.56)
                mb.add_tapered_tube(
                    points=[p0, p2],
                    radii=[0.062, 0.022],
                    cols_u8=[PAL["bark_dark"]],
                    segs=4,
                    cap_ends=False,
                )

            # Ana sarkik igne yaprak salkimi (kok govde icinde gizli -> cap_root=False)
            mb.add_pine_bough_cluster(
                origin=(dx * 0.02, dy * 0.02, wz + 0.04),
                dir_xy=(dx, dy),
                length=len_var,
                width=b_width,
                droop=droop,
                seed_val=seed,
                snowy=snowy,
                low_poly=True,
                cap_root=False,
            )
            seed += 1

    # Konik tepe surgunu (apical spire, Z = 3.60..4.70m, asla kutu degil!)
    for spire_ang in (0.0, 2.094, 4.189):
        mb.add_pine_bough_cluster(
            origin=(0.0, 0.0, 3.82),
            dir_xy=(math.cos(spire_ang), math.sin(spire_ang)),
            length=0.48,
            width=0.36,
            droop=0.26,
            seed_val=seed + int(spire_ang * 10),
            snowy=snowy,
            low_poly=True,
            cap_root=False,
        )
    mb.add_tapered_tube(
        points=[(0.0, 0.0, 3.60), (0.0, 0.0, 4.22), (0.0, 0.0, 4.70)],
        radii=[0.20, 0.10, 0.015],
        cols_u8=[
            PAL["snow_bright"] if snowy else PAL["pine_sun"],
            PAL["snow_shade"] if snowy else PAL["pine_tip"],
        ],
        segs=5,
    )
    return mb.finalize_object(mat, smooth_shading=False, preserve_face_smooth=True)


def build_flora_nordic_pine_canopy(mat):
    return _build_pine_canopy_common("Flora_Nordic_Pine_Canopy", snowy=False, mat=mat)


def build_flora_snow_pine_canopy(mat):
    return _build_pine_canopy_common("Flora_Snow_Pine_Canopy", snowy=True, mat=mat)


def build_flora_ancient_oak_canopy(mat):
    """
    Organik cok-kumeli, alttan gorunur catalli dalli kadim mese taci (asla kup veya tek kure degil!).
    Pivot (0,0,0) = CrownBaseY; kalin catalli mese kollari Z=-1.20m'den disa/yukari ayrilir,
    ayrik katmanli yaprak bulutlari Z=+1.05m .. +4.15m arasinda genis semsiye taci kurar.
    """
    mb = SculptedMeshBuilder("Flora_Ancient_Oak_Canopy")
    oak_pal = (PAL["oak_deep"], PAL["oak_mid"], PAL["oak_sun"], PAL["oak_warm_tip"])

    # Kalin bogumlu ust mese govdesi (alt dallarin ayrildigi ana catal -> ust tepeye kesintisiz uzanir)
    mb.add_tapered_tube(
        points=[(0.0, 0.0, -1.20), (0.04, -0.03, -0.10), (-0.02, 0.02, 1.15), (0.0, 0.0, 2.55)],
        radii=[0.38, 0.32, 0.22, 0.10],
        cols_u8=[PAL["bark_dark"], PAL["bark_ridge"], PAL["bark_dark"]],
        segs=5,
    )

    # 5 ana kivrimli dis mese dali + ikincil yan catallar + organik bulutsu yaprak kumeleri (subdivs=2)
    bough_specs = [
        (0.12, 2.55, 1.52, (1.14, 1.06, 0.58), True),
        (1.36, 2.42, 1.85, (1.08, 1.14, 0.60), False),
        (2.64, 2.62, 1.44, (1.16, 1.04, 0.56), True),
        (3.90, 2.38, 1.78, (1.06, 1.12, 0.60), False),
        (5.14, 2.50, 1.60, (1.12, 1.08, 0.58), False),
    ]
    for idx, (ang, reach, top_z, lobe_rad, add_sub) in enumerate(bough_specs):
        dx = math.cos(ang)
        dy = math.sin(ang)
        p0 = (dx * 0.14, dy * 0.14, -0.65 + (idx % 2) * 0.18)
        p1 = (dx * reach * 0.46 + dy * 0.18, dy * reach * 0.46 - dx * 0.18, top_z * 0.36)
        p2 = (dx * reach * 0.86, dy * reach * 0.86, top_z)
        mb.add_tapered_tube(
            points=[p0, p1, p2],
            radii=[0.20, 0.13, 0.065],
            cols_u8=[PAL["bark_dark"], PAL["bark_ridge"]],
            segs=4,
            cap_ends=False,
        )
        # Dis dal ucu yassi yaprak bulutu (subdivs=2, paylasilan yumusak vertex)
        mb.add_organic_foliage_lobe(
            center=(dx * reach * 0.94, dy * reach * 0.94, top_z + 0.22),
            radii=lobe_rad,
            seed_val=idx * 7 + 3,
            palette_quad=oak_pal,
            subdivs=2,
            smooth_shared=True,
        )
        if add_sub:
            sub_ang = ang + 0.64
            sdx, sdy = math.cos(sub_ang), math.sin(sub_ang)
            sub_end = (sdx * reach * 0.72, sdy * reach * 0.72, top_z * 0.82 + 0.55)
            mb.add_tapered_tube(
                points=[p1, sub_end],
                radii=[0.105, 0.050],
                cols_u8=[PAL["bark_ridge"]],
                segs=4,
                cap_ends=False,
            )

    # Ust tepe ve ara katman 4 bagli tac kubbesi (govde ve dallarla kesintisiz ortusur, havada kalmaz!)
    crown_caps = [
        ((-0.88, 0.56, 2.52), (1.14, 1.06, 0.60), 41),
        ((0.86, -0.58, 2.58), (1.12, 1.08, 0.60), 47),
        ((0.12, 0.92, 2.68), (1.06, 1.02, 0.56), 53),
        ((0.0, -0.05, 3.08), (1.42, 1.36, 0.72), 59),
    ]
    for c_idx, (ctr, rads, sd) in enumerate(crown_caps):
        if c_idx < 3:
            mb.add_tapered_tube(
                points=[(0.0, 0.0, 1.45), (ctr[0] * 0.75, ctr[1] * 0.75, ctr[2] - 0.18)],
                radii=[0.11, 0.05],
                cols_u8=[PAL["bark_dark"]],
                segs=4,
                cap_ends=False,
            )
        mb.add_organic_foliage_lobe(ctr, rads, sd, oak_pal, subdivs=2, smooth_shared=True)

    return mb.finalize_object(mat, smooth_shading=False, preserve_face_smooth=True)


def build_flora_tree_root_buttress(mat):
    """
    Agac dibi organik kok mahmuzlari & yosunlu govde bilezigi:
    Pivot (0,0,0) = agac tabani (WorldX, BaseY, WorldZ).
    Egimli arazide bosluk kalmamasi icin Z = -0.68m topragin altina iner, +1.18m govdeye sarilir.
    """
    mb = SculptedMeshBuilder("Flora_Tree_Root_Buttress")
    # Yosunlu alt govde kukasi (3 nokta, segs=6)
    mb.add_tapered_tube(
        points=[(0.0, 0.0, -0.65), (0.0, 0.0, 0.26), (0.0, 0.0, 1.18)],
        radii=[0.56, 0.48, 0.36],
        cols_u8=[PAL["bark_moss"], PAL["bark_dark"]],
        segs=6,
    )

    # 4 adet kivrimli kok mahmuzu (root spurs: 3 nokta, segs=4)
    for r_i in range(4):
        ang = (2.0 * math.pi * r_i) / 4.0 + 0.18 * math.sin(r_i * 2.7)
        dx = math.cos(ang)
        dy = math.sin(ang)
        perp_x, perp_y = -dy * 0.12, dx * 0.12
        pts = [
            (dx * 0.24, dy * 0.24, 0.92),
            (dx * 0.72 + perp_x, dy * 0.72 + perp_y, 0.18),
            (dx * 1.32, dy * 1.32, -0.62),
        ]
        mb.add_tapered_tube(
            points=pts,
            radii=[0.18, 0.20, 0.07],
            cols_u8=[PAL["bark_ridge"], PAL["bark_moss"]],
            segs=4,
        )
    return mb.finalize_object(mat, smooth_shading=False)


# ==============================================================================
# GLB IHRACI (0 DOKU, UV-FREE COLOR_0) & BLENDER ONIZLEME KARTLARI
# ==============================================================================


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


def setup_studio_render_rig():
    os.makedirs(OUT_PREVIEW_DIR, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = (
        "BLENDER_EEVEE_NEXT"
        if "BLENDER_EEVEE_NEXT"
        in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items.keys()
        else "BLENDER_EEVEE"
    )
    scene.render.resolution_x = 512
    scene.render.resolution_y = 512
    scene.render.image_settings.file_format = "PNG"

    world = bpy.data.worlds.new("CC0WorldFloraPreviewWorld")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs["Color"].default_value = (0.15, 0.18, 0.22, 1.0)
        bg.inputs["Strength"].default_value = 1.05

    sun_data = bpy.data.lights.new(name="SunKey", type="SUN")
    sun_data.energy = 3.0
    sun_obj = bpy.data.objects.new(name="SunKey", object_data=sun_data)
    sun_obj.rotation_euler = Euler((0.72, 0.25, 0.62), "XYZ")
    bpy.context.collection.objects.link(sun_obj)

    fill_data = bpy.data.lights.new(name="FillSun", type="SUN")
    fill_data.energy = 1.25
    fill_obj = bpy.data.objects.new(name="FillSun", object_data=fill_data)
    fill_obj.rotation_euler = Euler((1.05, -0.45, -2.25), "XYZ")
    bpy.context.collection.objects.link(fill_obj)

    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_obj = bpy.data.objects.new("PreviewCam", cam_data)
    bpy.context.collection.objects.link(cam_obj)
    scene.camera = cam_obj
    return scene, cam_obj, cam_data


def _compose_2x2_contact_sheet(temp_paths, out_sheet_path, sheet_name):
    sheet = bpy.data.images.new(sheet_name, width=1024, height=1024, alpha=True)
    sheet_pixels = [0.0] * (1024 * 1024 * 4)
    offsets = [(0, 512), (512, 512), (0, 0), (512, 0)]
    for path, (ox, oy) in zip(temp_paths, offsets):
        img = bpy.data.images.load(path)
        pix = list(img.pixels)
        for y in range(512):
            src_row = y * 512 * 4
            dst_row = ((oy + y) * 1024 + ox) * 4
            sheet_pixels[dst_row : dst_row + 512 * 4] = pix[src_row : src_row + 512 * 4]
        bpy.data.images.remove(img)
        try:
            os.remove(path)
        except OSError:
            pass
    sheet.pixels = sheet_pixels
    sheet.filepath_raw = out_sheet_path
    sheet.file_format = "PNG"
    sheet.save()
    bpy.data.images.remove(sheet)


def render_4_angle_sheet(scene, cam_obj, cam_data, target_obj, all_objs, asset_id):
    for o in all_objs:
        o.hide_render = o != target_obj

    bbox = [target_obj.matrix_world @ Vector(corner) for corner in target_obj.bound_box]
    min_v = Vector((min(p.x for p in bbox), min(p.y for p in bbox), min(p.z for p in bbox)))
    max_v = Vector((max(p.x for p in bbox), max(p.y for p in bbox), max(p.z for p in bbox)))
    center = 0.5 * (min_v + max_v)
    span = max(0.85, (max_v - min_v).length)
    dist = span * 1.38

    views = [
        ("front", center + Vector((0.0, -dist, span * 0.16)), center, 46.0),
        ("side", center + Vector((-dist, -dist * 0.15, span * 0.16)), center, 46.0),
        ("top", center + Vector((0.0, -dist * 0.22, dist * 1.08)), center, 46.0),
        ("iso", center + Vector((-dist * 0.74, -dist * 0.78, dist * 0.62)), center, 46.0),
    ]

    temp_paths = []
    for tag, pos, tgt, lens in views:
        cam_obj.location = pos
        cam_data.lens = lens
        direction = (tgt - pos).normalized()
        cam_obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
        out_p = os.path.join(OUT_PREVIEW_DIR, f"_tmp_{asset_id}_{tag}.png")
        scene.render.filepath = out_p
        bpy.ops.render.render(write_still=True)
        temp_paths.append(out_p)

    out_sheet = os.path.join(OUT_PREVIEW_DIR, f"{asset_id}_contact_sheet.png")
    _compose_2x2_contact_sheet(temp_paths, out_sheet, f"Sheet_{asset_id}")
    return out_sheet


def main():
    skip_previews = "--no-previews" in sys.argv
    flora_only = "--flora-only" in sys.argv
    clear_scene()
    mat = create_preview_vertex_color_material()

    modular_specs = [
        ("mod_log_beam_2m", build_mod_log_beam_2m),
        ("mod_log_pillar_3m", build_mod_log_pillar_3m),
        ("mod_plank_wall_2x2m", build_mod_plank_wall_2x2m),
        ("mod_plank_doorway_2x2m", build_mod_plank_doorway_2x2m),
        ("mod_plank_window_2x2m", build_mod_plank_window_2x2m),
        ("mod_plank_floor_2x2m", build_mod_plank_floor_2x2m),
        ("mod_stone_foundation_2x2m", build_mod_stone_foundation_2x2m),
        ("mod_stone_arch_pillar_3m", build_mod_stone_arch_pillar_3m),
        ("mod_roof_rafter_26deg_2x2m", build_mod_roof_rafter_26deg_2x2m),
        ("mod_roof_rafter_45deg_2x2m", build_mod_roof_rafter_45deg_2x2m),
        ("mod_roof_ridge_2m", build_mod_roof_ridge_2m),
        ("mod_diagonal_brace_2m", build_mod_diagonal_brace_2m),
        ("mod_gable_wall_45deg_2m", build_mod_gable_wall_45deg_2m),
    ]

    flora_specs = [
        ("flora_nordic_pine_canopy", build_flora_nordic_pine_canopy),
        ("flora_snow_pine_canopy", build_flora_snow_pine_canopy),
        ("flora_ancient_oak_canopy", build_flora_ancient_oak_canopy),
        ("flora_tree_root_buttress", build_flora_tree_root_buttress),
    ]

    built_all = []
    if not flora_only:
        for asset_id, builder_fn in modular_specs:
            obj = builder_fn(mat)
            out_glb = os.path.join(OUT_MODULAR_DIR, f"{asset_id}.glb")
            export_single_piece_glb(obj, out_glb)
            tris = len(obj.data.polygons)
            size_kb = os.path.getsize(out_glb) / 1024.0
            print(f"[CC0WorldFloraPipeline] Modular {asset_id}.glb ({tris} tris, {size_kb:.1f} KB)")
            built_all.append((asset_id, obj))

    for asset_id, builder_fn in flora_specs:
        obj = builder_fn(mat)
        out_glb = os.path.join(OUT_FLORA_DIR, f"{asset_id}.glb")
        export_single_piece_glb(obj, out_glb)
        tris = len(obj.data.polygons)
        size_kb = os.path.getsize(out_glb) / 1024.0
        print(f"[CC0WorldFloraPipeline] Flora {asset_id}.glb ({tris} tris, {size_kb:.1f} KB)")
        built_all.append((asset_id, obj))

    if not skip_previews:
        key_preview_ids = {
            "mod_gable_wall_45deg_2m",
            "mod_plank_window_2x2m",
            "mod_stone_foundation_2x2m",
            "mod_stone_arch_pillar_3m",
            "mod_roof_rafter_45deg_2x2m",
            "flora_nordic_pine_canopy",
            "flora_snow_pine_canopy",
            "flora_ancient_oak_canopy",
            "flora_tree_root_buttress",
        }
        scene, cam_obj, cam_data = setup_studio_render_rig()
        all_objs = [o for _, o in built_all]
        for asset_id, obj in built_all:
            if asset_id in key_preview_ids:
                sheet = render_4_angle_sheet(scene, cam_obj, cam_data, obj, all_objs, asset_id)
                print(f"[CC0WorldFloraPipeline] Preview rendered: {sheet}")


if __name__ == "__main__":
    main()
