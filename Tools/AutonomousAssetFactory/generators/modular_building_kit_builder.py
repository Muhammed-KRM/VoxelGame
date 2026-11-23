"""
Stratocraft Otonom 3B Fabrika — Valheim & Sons of the Forest Moduler 3B Yapi Seti Ureticisi (Blender 4.2).

Kullanim (PATH uzerindeki blender ile, 0 gomulu doku, UV-free COLOR_0):
    blender --background --python Tools/AutonomousAssetFactory/generators/modular_building_kit_builder.py

Uretilen 12 Moduler .glb Parca (Assets/Art/Generated/Structures_Modular/*.glb):
    1. mod_log_beam_2m.glb            — Soyulmus yuvarlak kutuk kiris (Ø 0.26m, 2.0m, eyer centikli)
    2. mod_log_pillar_3m.glb          — Soyulmus yuvarlak tasiyici kutuk dikme (Ø 0.26m, 3.0m, eyer centikli)
    3. mod_plank_wall_2x2m.glb        — Bindirmeli kalas duvar + dikme karkas (2x2m, 0.12m kalinlik)
    4. mod_plank_doorway_2x2m.glb     — Bindirmeli kalas kapili duvar + esik/lento/sove (2x2m, 0.12m)
    5. mod_plank_window_2x2m.glb      — Bindirmeli kalas pencereli duvar + denizlik/kayit (2x2m, 0.12m)
    6. mod_plank_floor_2x2m.glb       — Alt kirisli (joist) doseme tahtasi (2x2m, 0.10m kalinlik)
    7. mod_stone_foundation_2x2m.glb  — Duzensiz harcli tarla tasi kaide/temel (2x2m, 0.32m kalinlik)
    8. mod_stone_arch_pillar_3m.glb   — Harcli tas sutun + oymali kemer basligi (3.0m yukseklik, ~0.34m)
    9. mod_roof_rafter_26deg_2x2m.glb — 26° (atan 0.5) padavra/zona kapli mertekli cati (2x2m, 0.14m)
   10. mod_roof_rafter_45deg_2x2m.glb — 45° dik İskandinav zona kapli mertekli cati (2x2m, 0.14m)
   11. mod_roof_ridge_2m.glb          — 2.0m mahya kapagi + oyma mahya asigi (0.14m kalinlik)
   12. mod_diagonal_brace_2m.glb      — 45° capraz payanda / gogusleme kirisi (2.0m diyagonal)

Denetim Kartlari:
    - BuildLogs/Previews/<asset_id>_contact_sheet.png (4 acili 512x512 -> 1024x1024 PNG)
    - BuildLogs/Previews/modular_kit_overview_contact_sheet.png (tum set + ornek kulube onizlemesi)
"""

import math
import os

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, "..", "..", ".."))
OUT_MODULAR_DIR = os.path.join(REPO_ROOT, "Assets", "Art", "Generated", "Structures_Modular")
OUT_PREVIEW_DIR = os.path.join(REPO_ROOT, "BuildLogs", "Previews")


# Dogal Kuzey Ahsabi, Kabuk, Harcli Tarla Tasi ve Cati Zonasi Renk Paleti (sRGB 0..255 -> glTF COLOR_0)
# Not: R kanali asla 0, 3 veya 11 secilmez (StratocraftUberShader ruzgar/magma maskeleriyle %100 ayrik).
PAL = {
    # Soyulmus ladin/cam kutuk & kesim yuzeyi
    "log_peeled_warm": (196, 148, 92, 248),
    "log_peeled_mid": (178, 130, 78, 244),
    "log_peeled_shade": (148, 104, 60, 224),
    "log_notch_carve": (208, 164, 108, 232),
    "log_endgrain_outer": (214, 174, 118, 250),
    "log_endgrain_heart": (162, 112, 66, 242),
    "bark_dark": (78, 54, 38, 218),
    "bark_ridge": (96, 68, 48, 228),
    # Bicilmis kalas, karkas dikme ve doseme tahtasi
    "plank_light": (174, 126, 78, 246),
    "plank_mid": (156, 110, 66, 242),
    "plank_warm": (166, 116, 70, 244),
    "plank_dark": (132, 90, 52, 228),
    "stud_frame": (118, 78, 44, 232),
    "sill_weathered": (126, 92, 62, 236),
    "joist_heavy": (108, 72, 42, 222),
    # Demir civi / kenet pimi
    "iron_hardware": (66, 70, 78, 235),
    "wood_peg": (188, 144, 88, 246),
    # Harcli tarla tasi ve yontma kemer tasi
    "mortar_bed": (116, 114, 108, 196),
    "stone_warm": (146, 138, 126, 244),
    "stone_cool": (128, 132, 138, 242),
    "stone_dark": (98, 100, 104, 228),
    "stone_light": (164, 158, 148, 248),
    "stone_moss_tint": (114, 122, 102, 232),
    "arch_carved": (172, 166, 156, 248),
    "arch_keystone": (186, 178, 166, 252),
    # Cati zonasi (katranli/yaslanmis cam ve arduvaz zona)
    "shingle_dark": (68, 54, 44, 236),
    "shingle_mid": (84, 66, 52, 242),
    "shingle_warm": (96, 74, 56, 244),
    "shingle_slate": (74, 76, 82, 240),
    "rafter_timber": (138, 96, 56, 234),
    # Ekol 1-3 Yeni Mimari Malzemeler (Chinking, Wattle & Daub, Kerpic, Kirec Harci, Kesme Tas)
    "chink_moss": (108, 124, 82, 236),
    "chink_clay": (168, 146, 114, 240),
    "wattle_hazel": (146, 104, 62, 238),
    "daub_plaster": (216, 198, 168, 246),
    "daub_shade": (194, 176, 146, 240),
    "adobe_brick_warm": (192, 134, 92, 244),
    "adobe_brick_mid": (176, 120, 80, 242),
    "adobe_mortar": (154, 112, 78, 228),
    "lime_mortar_light": (204, 198, 186, 236),
    "ashlar_limestone": (188, 182, 170, 248),
    "ashlar_warm": (176, 168, 154, 246),
    "basalt_lintel": (92, 96, 102, 244),
}


def srgb_to_linear_rgba(rgba_u8):
    """Blender 4.2 BYTE_COLOR/FLOAT_COLOR -> glTF COLOR_0 icin kesin sRGB->Linear donusumu."""

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
    mat = bpy.data.materials.new(name="M_ModularVertexColorPreview")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    vcol = nt.nodes.new("ShaderNodeVertexColor")
    vcol.layer_name = "Col"
    bsdf.inputs["Roughness"].default_value = 0.76
    bsdf.inputs["Specular IOR Level"].default_value = 0.22
    nt.links.new(vcol.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


class ModularMeshBuilder:
    """
    UV-free COLOR_0 moduler mimari parca bmesh insacisi.
    Koordinat eslemesi (export_yup=True + GlbHierarchyLoader.LoadArticulatedPartsFromDisk):
      Blender +Z -> Unity +Y (Yukari)
      Blender -Y -> Unity +Z (Ileri)
      Blender -X -> Unity +X (Sag)
    """

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
            # Alt yuzeylerde ve zemine yakin kisimlarda dogal ortam kapanmasi (AO) ve hafif isik gradyani
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

    def add_box(self, center, half_extents, rgba_u8, rot_euler=(0.0, 0.0, 0.0), bevel_amt=0.008):
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
        bevel_w = min(radii) * 0.28
        bmesh.ops.bevel(self.bm, geom=edges, offset=bevel_w, segments=1, affect="EDGES")

        new_verts = [v for v in self.bm.verts if v not in prev_verts and v.is_valid]
        c_vec = Vector(center)
        for idx, v in enumerate(new_verts):
            rel = v.co - c_vec
            h = math.sin((idx + 1) * 12.9898 + seed_val * 78.233) * 43758.5453
            frac = h - math.floor(h)
            jitter = 0.90 + 0.18 * frac
            v.co = c_vec + Vector((rel.x * jitter, rel.y * jitter, rel.z * (0.93 + 0.12 * frac)))

        new_faces = [f for f in self.bm.faces if f not in prev_faces and f.is_valid]
        bmesh.ops.recalc_face_normals(self.bm, faces=new_faces)
        self.paint_faces(new_faces, rgba_u8, z_ao_origin=0.0, z_ao_span=0.5, z_ao_strength=0.12)
        return new_faces

    def add_notched_log(
        self,
        length,
        radius,
        notch_positions,
        along_axis="X",
        center_offset=(0.0, 0.0, 0.0),
        segs=14,
    ):
        """
        Soyulmus yuvarlak ladin/cam kutuk (Ø = 2*radius = 0.26m):
        - Uclarda kabuk halkasi + ic oz odun kesit halkasi
        - notch_positions konumlarinda oyulmus Valheim/SOTF eyer centigi (saddle notch)
        """
        half_l = length * 0.5
        # Kutuk boyunca istasyonlar olustur; eyer centigi bolgelerinde yaricapi bogumla daralt
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
            # Uclara yakin (son 0.10m) ince kabuk bilezigi, govdede soyulmus altin-kehribar kutuk
            dist_end = half_l - abs(t)
            is_bark_collar = 0.035 < dist_end < 0.115
            notch_depth = 0.0
            for np_pos in notch_positions:
                d = abs(t - np_pos)
                if d < 0.145:
                    notch_depth = max(notch_depth, math.cos((d / 0.145) * (math.pi * 0.5)))

            col = PAL["bark_dark"] if is_bark_collar else (
                PAL["log_notch_carve"] if notch_depth > 0.35 else PAL["log_peeled_warm"]
            )
            ring_cols.append(col)

            rv = []
            for s in range(segs):
                ang = (2.0 * math.pi * s) / segs
                ca = math.cos(ang)
                sa = math.sin(ang)
                # Dogal agac govdesi hafif organik dalgalanma
                organic = 1.0 + 0.028 * math.sin(ang * 3.0 + t * 4.1) + 0.018 * math.cos(ang * 5.0 - t * 2.7)
                r_local = radius * organic
                if is_bark_collar:
                    r_local *= 1.035
                elif dist_end < 0.015:
                    r_local *= 0.95  # Uclarda pah kirma (chamfer)

                # Ust ve alt eyer centigi (U-oyuk)
                if notch_depth > 0.01:
                    vert_align = abs(sa)  # sa=+1 ust, sa=-1 alt
                    if vert_align > 0.38:
                        carve = ((vert_align - 0.38) / 0.62) * 0.34 * notch_depth
                        r_local *= (1.0 - carve)

                if along_axis == "X":
                    pt = Vector((t, ca * r_local, sa * r_local)) + off
                else:
                    # Dikey kutuk dikme (+Z yukari, Z in [0..length])
                    z_val = t + half_l
                    pt = Vector((ca * r_local, sa * r_local, z_val)) + off
                rv.append(self.bm.verts.new(pt))
            ring_verts.append(rv)

        for i in range(len(ring_verts) - 1):
            r0 = ring_verts[i]
            r1 = ring_verts[i + 1]
            band_faces = []
            for s in range(segs):
                sn = (s + 1) % segs
                try:
                    f = self.bm.faces.new((r0[s], r0[sn], r1[sn], r1[s]))
                    band_faces.append(f)
                except ValueError:
                    pass
            bmesh.ops.recalc_face_normals(self.bm, faces=band_faces)
            self.paint_faces(band_faces, ring_cols[i])

        # Her iki ucta ic oz odun halkali (concentric end-grain) kapak
        for end_idx, flip in ((0, True), (-1, False)):
            outer_ring = ring_verts[end_idx]
            center_pt = sum((v.co for v in outer_ring), Vector((0.0, 0.0, 0.0))) / len(outer_ring)
            inner_ring = [
                self.bm.verts.new(center_pt.lerp(v.co, 0.48))
                for v in outer_ring
            ]
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
# 12 MODULER PARCA URETICILERI
# ==============================================================================

def build_mod_log_beam_2m(mat):
    """Ø 0.26m (r=0.13m), 2.08m uclu yatay kutuk kiris; X=±0.90m eyer centikli, tabani Z=0.0m."""
    mb = ModularMeshBuilder("Mod_Log_Beam_2m")
    mb.add_notched_log(
        length=2.08,
        radius=0.13,
        notch_positions=(-0.90, 0.90),
        along_axis="X",
        center_offset=(0.0, 0.0, 0.13),
        segs=14,
    )
    # Eyer centigi yanlarina ahsap kavela / demir kenet pimleri
    for sx in (-0.90, 0.90):
        mb.add_box((sx, 0.0, 0.245), (0.022, 0.022, 0.018), PAL["wood_peg"], bevel_amt=0.004)
    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_log_pillar_3m(mat):
    """Ø 0.26m (r=0.13m), 3.0m dikey soyulmus kutuk sutun; alt/orta/ust kiris eyer centikli."""
    mb = ModularMeshBuilder("Mod_Log_Pillar_3m")
    mb.add_notched_log(
        length=3.00,
        radius=0.13,
        notch_positions=(-1.28, 0.42, 1.32),
        along_axis="Z",
        center_offset=(0.0, 0.0, 0.0),
        segs=14,
    )
    # Alt nem kesici tas/demir papuc bilezigi
    mb.add_box((0.0, 0.0, 0.03), (0.142, 0.142, 0.03), PAL["iron_hardware"], bevel_amt=0.006)
    return mb.finalize_object(mat, smooth_shading=False)


def _add_clapboard_span(mb, x0, x1, z0, z1, row_count):
    """Verilen X ve Z araligina iki yuzlu bindirmeli (clapboard) 0.12m duvar kalaslari dizer."""
    cx = 0.5 * (x0 + x1)
    hx = 0.5 * (x1 - x0)
    if hx <= 0.02 or z1 <= z0 + 0.02:
        return
    step_z = (z1 - z0) / row_count
    tones = (PAL["plank_light"], PAL["plank_mid"], PAL["plank_warm"], PAL["plank_dark"])
    for r in range(row_count):
        cz = z0 + (r + 0.5) * step_z
        hz = step_z * 0.53
        col = tones[r % len(tones)]
        # On ve arka bindirmeli yuzey kalaslari (hafif egimli bindirme -> toplam kalinlik ~0.12m)
        mb.add_box(
            (cx, -0.034, cz), (hx, 0.024, hz), col, rot_euler=(0.06, 0.0, 0.0), bevel_amt=0.005
        )
        mb.add_box(
            (cx, 0.034, cz),
            (hx, 0.024, hz),
            tones[(r + 1) % len(tones)],
            rot_euler=(-0.06, 0.0, 0.0),
            bevel_amt=0.005,
        )


def build_mod_plank_wall_2x2m(mat):
    """2.0m x 2.0m, 0.12m kalinlikta karkas dikmeli ve bindirmeli ahsap kalas duvar."""
    mb = ModularMeshBuilder("Mod_Plank_Wall_2x2m")
    # Alt taban asigi, ust baslik kirisi ve sol/orta/sag dikmeler (0.12m zarf)
    mb.add_box((0.0, 0.0, 0.045), (1.00, 0.060, 0.045), PAL["stud_frame"], bevel_amt=0.006)
    mb.add_box((0.0, 0.0, 1.955), (1.00, 0.060, 0.045), PAL["stud_frame"], bevel_amt=0.006)
    mb.add_box((-0.945, 0.0, 1.00), (0.055, 0.060, 0.92), PAL["stud_frame"], bevel_amt=0.006)
    mb.add_box((0.945, 0.0, 1.00), (0.055, 0.060, 0.92), PAL["stud_frame"], bevel_amt=0.006)
    mb.add_box((0.0, 0.0, 1.00), (0.045, 0.054, 0.92), PAL["stud_frame"], bevel_amt=0.005)
    # 9 sira bindirmeli kalas (sol ve sag bolmeler)
    _add_clapboard_span(mb, -0.89, -0.045, 0.09, 1.91, 9)
    _add_clapboard_span(mb, 0.045, 0.89, 0.09, 1.91, 9)
    # Demir civi baslari
    for sx in (-0.945, 0.0, 0.945):
        for sz in (0.35, 1.00, 1.65):
            mb.add_box((sx, -0.062, sz), (0.012, 0.006, 0.012), PAL["iron_hardware"], bevel_amt=0.002)
            mb.add_box((sx, 0.062, sz), (0.012, 0.006, 0.012), PAL["iron_hardware"], bevel_amt=0.002)
    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_plank_doorway_2x2m(mat):
    """2.0m x 2.0m, 0.12m kalinlikta yuruyerek gecilebilir kemerli/lentolu ahsap kapili duvar."""
    mb = ModularMeshBuilder("Mod_Plank_Doorway_2x2m")
    # Dis yan dikmeler + ust baslik + alcak esik (threshold sill)
    mb.add_box((-0.945, 0.0, 1.00), (0.055, 0.060, 1.00), PAL["stud_frame"], bevel_amt=0.006)
    mb.add_box((0.945, 0.0, 1.00), (0.055, 0.060, 1.00), PAL["stud_frame"], bevel_amt=0.006)
    mb.add_box((0.0, 0.0, 1.955), (0.89, 0.060, 0.045), PAL["stud_frame"], bevel_amt=0.006)
    mb.add_box((-0.72, 0.0, 0.045), (0.22, 0.060, 0.045), PAL["stud_frame"], bevel_amt=0.006)
    mb.add_box((0.72, 0.0, 0.045), (0.22, 0.060, 0.045), PAL["stud_frame"], bevel_amt=0.006)
    mb.add_box((0.0, 0.0, 0.022), (0.50, 0.068, 0.022), PAL["sill_weathered"], bevel_amt=0.005)
    # Kapi sove dikmeleri (X = ±0.48m, 0.90m temiz aciklik) ve kalin lento kirisi (Z = 1.74m)
    mb.add_box((-0.49, 0.0, 0.89), (0.050, 0.066, 0.85), PAL["stud_frame"], bevel_amt=0.006)
    mb.add_box((0.49, 0.0, 0.89), (0.050, 0.066, 0.85), PAL["stud_frame"], bevel_amt=0.006)
    mb.add_box((0.0, 0.0, 1.75), (0.54, 0.070, 0.055), PAL["sill_weathered"], bevel_amt=0.007)
    # Ust kose gogusleme takozlari (Nordic doorway gussets)
    mb.add_box(
        (-0.37, 0.0, 1.62),
        (0.11, 0.052, 0.035),
        PAL["plank_dark"],
        rot_euler=(0.0, 0.65, 0.0),
        bevel_amt=0.004,
    )
    mb.add_box(
        (0.37, 0.0, 1.62),
        (0.11, 0.052, 0.035),
        PAL["plank_dark"],
        rot_euler=(0.0, -0.65, 0.0),
        bevel_amt=0.004,
    )
    # Sol, sag ve lento ustu kalas dolgular
    _add_clapboard_span(mb, -0.89, -0.54, 0.09, 1.91, 9)
    _add_clapboard_span(mb, 0.54, 0.89, 0.09, 1.91, 9)
    _add_clapboard_span(mb, -0.54, 0.54, 1.805, 1.91, 1)
    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_plank_window_2x2m(mat):
    """2.0m x 2.0m, 0.12m kalinlikta cikintili denizlikli ve orta kayitli pencereli kalas duvar."""
    mb = ModularMeshBuilder("Mod_Plank_Window_2x2m")
    # Ana cerceve
    mb.add_box((0.0, 0.0, 0.045), (1.00, 0.060, 0.045), PAL["stud_frame"], bevel_amt=0.006)
    mb.add_box((0.0, 0.0, 1.955), (1.00, 0.060, 0.045), PAL["stud_frame"], bevel_amt=0.006)
    mb.add_box((-0.945, 0.0, 1.00), (0.055, 0.060, 0.92), PAL["stud_frame"], bevel_amt=0.006)
    mb.add_box((0.945, 0.0, 1.00), (0.055, 0.060, 0.92), PAL["stud_frame"], bevel_amt=0.006)
    # Pencere cercevesi: X in [-0.48..+0.48], Z in [0.84..1.56] + disari tasan damlalikli denizlik (sill)
    mb.add_box((-0.51, 0.0, 1.20), (0.045, 0.064, 0.40), PAL["stud_frame"], bevel_amt=0.005)
    mb.add_box((0.51, 0.0, 1.20), (0.045, 0.064, 0.40), PAL["stud_frame"], bevel_amt=0.005)
    mb.add_box((0.0, 0.0, 0.81), (0.58, 0.082, 0.040), PAL["sill_weathered"], bevel_amt=0.006)
    mb.add_box((0.0, 0.0, 1.59), (0.57, 0.072, 0.042), PAL["sill_weathered"], bevel_amt=0.006)
    # Orta dikey pencere kaydi (mullion)
    mb.add_box((0.0, 0.0, 1.20), (0.024, 0.042, 0.35), PAL["plank_dark"], bevel_amt=0.004)
    # Alt etek, sol/sag yanlar ve ust alin kalaslari
    _add_clapboard_span(mb, -0.89, 0.89, 0.09, 0.77, 4)
    _add_clapboard_span(mb, -0.89, -0.555, 0.77, 1.91, 6)
    _add_clapboard_span(mb, 0.555, 0.89, 0.77, 1.91, 6)
    _add_clapboard_span(mb, -0.555, 0.555, 1.63, 1.91, 2)
    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_plank_floor_2x2m(mat):
    """2.0m x 2.0m, 0.10m kalinlikta 3 alt tasiyici kiris (joist) uzerine dizilmis 8 doseme tahtasi."""
    mb = ModularMeshBuilder("Mod_Plank_Floor_2x2m")
    # 3 alt tasiyici joist kirisi (Z in [0.00..0.06], Y ekseni boyunca uzanir)
    for jx in (-0.88, 0.0, 0.88):
        mb.add_box((jx, 0.0, 0.030), (0.055, 0.99, 0.030), PAL["joist_heavy"], bevel_amt=0.005)
    # Cevre kusak cercevesi
    mb.add_box((0.0, -0.955, 0.030), (0.82, 0.042, 0.030), PAL["joist_heavy"], bevel_amt=0.004)
    mb.add_box((0.0, 0.955, 0.030), (0.82, 0.042, 0.030), PAL["joist_heavy"], bevel_amt=0.004)
    # Ust 8 genis doseme kalasi (Z in [0.056..0.100], hafif derz bosluklu)
    tones = (PAL["plank_light"], PAL["plank_warm"], PAL["plank_mid"], PAL["plank_dark"])
    board_count = 8
    step_y = 2.0 / board_count
    for b in range(board_count):
        cy = -1.0 + (b + 0.5) * step_y
        mb.add_box((0.0, cy, 0.078), (0.996, step_y * 0.478, 0.022), tones[b % len(tones)], bevel_amt=0.005)
        for nx in (-0.88, 0.0, 0.88):
            mb.add_box((nx, cy, 0.101), (0.011, 0.011, 0.003), PAL["iron_hardware"], bevel_amt=0.001)
    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_stone_foundation_2x2m(mat):
    """2.0m x 2.0m, 0.32m (+0.06m toprak alti etek) harcli duzensiz tarla tasi kaide/temel."""
    mb = ModularMeshBuilder("Mod_Stone_Foundation_2x2m")
    # Iceride geri cekilmis kirec harci cekirdek yatagi (Z in [-0.05..0.295])
    mb.add_box((0.0, 0.0, 0.125), (0.94, 0.94, 0.175), PAL["mortar_bed"], bevel_amt=0.012)

    stone_tones = (
        PAL["stone_warm"],
        PAL["stone_cool"],
        PAL["stone_light"],
        PAL["stone_dark"],
        PAL["stone_moss_tint"],
    )
    seed = 1
    # 4 kose buyuk baglama taslari (corner quoins)
    for kx in (-0.79, 0.79):
        for ky in (-0.79, 0.79):
            mb.add_irregular_stone(
                (kx, ky, 0.06), (0.22, 0.22, 0.11), stone_tones[seed % len(stone_tones)], seed
            )
            seed += 1
            mb.add_irregular_stone(
                (kx * 0.98, ky * 0.98, 0.225), (0.21, 0.21, 0.095), stone_tones[seed % len(stone_tones)], seed
            )
            seed += 1

    # 4 yan cephe boyunca cift sira duzensiz harcli tarla taslari
    edge_offsets = (-0.42, 0.0, 0.42)
    for eo in edge_offsets:
        for side_sign in (-1.0, 1.0):
            # On/Arka kenarlar
            mb.add_irregular_stone(
                (eo, side_sign * 0.83, 0.065), (0.20, 0.17, 0.105), stone_tones[seed % len(stone_tones)], seed
            )
            seed += 1
            mb.add_irregular_stone(
                (eo + 0.06 * side_sign, side_sign * 0.82, 0.225),
                (0.19, 0.165, 0.092),
                stone_tones[seed % len(stone_tones)],
                seed,
            )
            seed += 1
            # Sol/Sag kenarlar
            mb.add_irregular_stone(
                (side_sign * 0.83, eo, 0.065), (0.17, 0.20, 0.105), stone_tones[seed % len(stone_tones)], seed
            )
            seed += 1
            mb.add_irregular_stone(
                (side_sign * 0.82, eo - 0.05 * side_sign, 0.225),
                (0.165, 0.19, 0.092),
                stone_tones[seed % len(stone_tones)],
                seed,
            )
            seed += 1

    # Ust yuzey genis yassi kayrak doseme taslari (Z ust = ~0.32m)
    for gx in (-0.42, 0.0, 0.42):
        for gy in (-0.42, 0.0, 0.42):
            mb.add_box(
                (gx, gy, 0.298),
                (0.195, 0.195, 0.024),
                stone_tones[seed % len(stone_tones)],
                rot_euler=(0.0, 0.0, ((seed % 5) - 2) * 0.025),
                bevel_amt=0.010,
            )
            seed += 1
    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_stone_arch_pillar_3m(mat):
    """3.0m yuksekliginde harcli tas sutun + kademeli kaide + iki yana acilan oymali kemer basligi."""
    mb = ModularMeshBuilder("Mod_Stone_Arch_Pillar_3m")
    # Kademeli genis tas kaide (Z in [0.0..0.42])
    mb.add_box((0.0, 0.0, 0.09), (0.23, 0.23, 0.09), PAL["stone_dark"], bevel_amt=0.014)
    mb.add_box((0.0, 0.0, 0.25), (0.195, 0.195, 0.08), PAL["arch_carved"], bevel_amt=0.012)
    # Harc derzli tas govde bloklari (0.32m x 0.32m kesit, Z in [0.33..2.36])
    tones = (PAL["arch_carved"], PAL["stone_light"], PAL["stone_warm"], PAL["stone_cool"])
    mb.add_box((0.0, 0.0, 1.35), (0.142, 0.142, 1.02), PAL["mortar_bed"], bevel_amt=0.006)
    drum_count = 7
    z_start = 0.34
    z_span = 2.02
    dz = z_span / drum_count
    for d in range(drum_count):
        cz = z_start + (d + 0.5) * dz
        yaw = 0.03 if (d % 2 == 1) else -0.02
        mb.add_irregular_stone(
            (0.0, 0.0, cz),
            (0.165, 0.165, dz * 0.45),
            tones[d % len(tones)],
            d + 11,
            (0.0, 0.0, yaw),
        )

    # Ust impost baslik + iki yana (±X) yayilan Romanesk kemer uzengi taslari (Z in [2.36..3.00])
    mb.add_box((0.0, 0.0, 2.44), (0.21, 0.20, 0.075), PAL["arch_keystone"], bevel_amt=0.012)
    mb.add_box((0.0, 0.0, 2.74), (0.19, 0.175, 0.25), PAL["arch_carved"], bevel_amt=0.012)
    for sign in (-1.0, 1.0):
        mb.add_box(
            (sign * 0.24, 0.0, 2.66),
            (0.14, 0.16, 0.11),
            PAL["stone_light"],
            rot_euler=(0.0, sign * 0.42, 0.0),
            bevel_amt=0.010,
        )
        mb.add_box(
            (sign * 0.40, 0.0, 2.86),
            (0.16, 0.155, 0.115),
            PAL["arch_keystone"],
            rot_euler=(0.0, sign * 0.78, 0.0),
            bevel_amt=0.010,
        )
    return mb.finalize_object(mat, smooth_shading=False)


def _build_pitched_shingle_roof(name, rise_height, shingle_rows, mat):
    """
    2.0m genislik (X in [-1..+1]), 2.0m yatay aciklik:
    Blender Y=+1.0, Z=0.0 (Unity Z=-1.0 sacak) -> Blender Y=-1.0, Z=+rise_height (Unity Z=+1.0 mahya).
    Toplam kesit kalinligi = 0.14m (alt mertekler + asiklar + ust bindirmeli zona sirasi).
    """
    mb = ModularMeshBuilder(name)
    slope_angle = math.atan2(rise_height, 2.0)  # 26.565 deg (0.4636 rad) veya 45 deg (0.7854 rad)
    slope_len = math.sqrt(4.0 + rise_height * rise_height)
    # Egim boyunca birim teget (sacak -> mahya: dy < 0, dz > 0) ve ust normal vektoru
    tang = Vector((0.0, -2.0 / slope_len, rise_height / slope_len))
    up_n = Vector((0.0, rise_height / slope_len, 2.0 / slope_len))
    # Blender Euler X donusu: +Y eksenini asagi (-Z) dondurdugu icin pitch = -slope_angle
    rot_x = -slope_angle

    # Sacakta 0.08m damlalik tasmasi -> s in [-0.08 .. slope_len + 0.02]
    total_s = slope_len + 0.10
    mid_s = 0.5 * (slope_len - 0.06)
    eave_origin = Vector((0.0, 1.0, 0.0))

    # 1) 4 adet alt tasiyici mertek (rafter, 0.08m yukseklik)
    for rx in (-0.92, -0.31, 0.31, 0.92):
        c_raf = eave_origin + tang * mid_s + up_n * 0.042 + Vector((rx, 0.0, 0.0))
        mb.add_box(
            c_raf,
            (0.045, total_s * 0.5, 0.040),
            PAL["rafter_timber"],
            rot_euler=(rot_x, 0.0, 0.0),
            bevel_amt=0.005,
        )

    # 2) Alt kaplama tahtasi / asik katmani (sheathing)
    c_deck = eave_origin + tang * mid_s + up_n * 0.092
    mb.add_box(
        c_deck,
        (0.995, total_s * 0.5, 0.016),
        PAL["plank_dark"],
        rot_euler=(rot_x, 0.0, 0.0),
        bevel_amt=0.004,
    )

    # 3) Bindirmeli ahsap/arduvaz zona siralari (staggered shingle courses -> ~0.14m toplam kalinlik)
    shingle_tones = (PAL["shingle_dark"], PAL["shingle_mid"], PAL["shingle_warm"], PAL["shingle_slate"])
    step_s = total_s / shingle_rows
    tiles_per_row = 6
    tile_w = 2.0 / tiles_per_row
    for r in range(shingle_rows):
        s_pos = -0.06 + (r + 0.5) * step_s
        # Her zona sirasi bir alttaki siranin uzerine hafif dik aciyla biner
        overlap_tilt = rot_x + 0.055
        stagger = (tile_w * 0.25) if (r % 2 == 1) else -(tile_w * 0.25)
        for t_idx in range(tiles_per_row):
            raw_x = -1.0 + (t_idx + 0.5) * tile_w + stagger
            x0 = max(-1.0, raw_x - tile_w * 0.49)
            x1 = min(1.0, raw_x + tile_w * 0.49)
            if x1 - x0 < 0.06:
                continue
            cx = 0.5 * (x0 + x1)
            hx = 0.5 * (x1 - x0) - 0.004
            c_sh = eave_origin + tang * s_pos + up_n * 0.124 + Vector((cx, 0.0, 0.0))
            tone = shingle_tones[(r * 3 + t_idx) % len(shingle_tones)]
            mb.add_box(
                c_sh,
                (hx, step_s * 0.56, 0.018),
                tone,
                rot_euler=(overlap_tilt, 0.0, 0.0),
                bevel_amt=0.004,
            )

    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_roof_rafter_26deg_2x2m(mat):
    return _build_pitched_shingle_roof("Mod_Roof_Rafter_26deg_2x2m", rise_height=1.0, shingle_rows=7, mat=mat)


def build_mod_roof_rafter_45deg_2x2m(mat):
    return _build_pitched_shingle_roof("Mod_Roof_Rafter_45deg_2x2m", rise_height=2.0, shingle_rows=9, mat=mat)


def build_mod_roof_ridge_2m(mat):
    """2.0m uzunlugunda ters-V (Λ) eyer kanatli mahya zona kapagi + ust oymali İskandinav mahya kutugu."""
    mb = ModularMeshBuilder("Mod_Roof_Ridge_2m")
    # Ust merkez mahya asigi (X in [-1.02..+1.02], tepe altinda Z=0.12)
    mb.add_box((0.0, 0.0, 0.125), (1.02, 0.065, 0.050), PAL["rafter_timber"], bevel_amt=0.006)
    # Iki yana ASAGI egimli (ters-V / Λ eyer) 0.14m mahya kanatlari + parcali zona kapaklari
    for sign in (-1.0, 1.0):
        mb.add_box(
            (0.0, sign * 0.165, 0.082),
            (1.01, 0.215, 0.024),
            PAL["plank_dark"],
            rot_euler=(-sign * 0.58, 0.0, 0.0),
            bevel_amt=0.005,
        )
        for k in range(6):
            cx = -1.0 + (k + 0.5) * (2.0 / 6.0)
            tone = PAL["shingle_mid"] if (k % 2 == 0) else PAL["shingle_dark"]
            mb.add_box(
                (cx, sign * 0.175, 0.112),
                (0.16, 0.220, 0.020),
                tone,
                rot_euler=(-sign * 0.58, 0.0, 0.0),
                bevel_amt=0.004,
            )
    # Ust yuvarlatilmis oyma mahya omurgasi + uclarda capraz Iskandinav kalkan suslemesi
    mb.add_box(
        (0.0, 0.0, 0.205),
        (1.03, 0.052, 0.052),
        PAL["log_peeled_warm"],
        rot_euler=(0.785, 0.0, 0.0),
        bevel_amt=0.008,
    )
    for ex in (-0.98, 0.98):
        mb.add_box(
            (ex, 0.0, 0.245),
            (0.028, 0.135, 0.028),
            PAL["sill_weathered"],
            rot_euler=(0.65, 0.0, 0.0),
            bevel_amt=0.004,
        )
        mb.add_box(
            (ex, 0.0, 0.245),
            (0.028, 0.135, 0.028),
            PAL["sill_weathered"],
            rot_euler=(-0.65, 0.0, 0.0),
            bevel_amt=0.004,
        )
    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_diagonal_brace_2m(mat):
    """
    45° capraz gogusleme / payanda kirisi (2.0m diyagonal uzunluk):
    Unity yerel uzayda (-0.70m, 0.0m, 0) -> (+0.70m, +1.40m, 0) (Blender: +0.70,0,0 -> -0.70,0,1.40).
    """
    mb = ModularMeshBuilder("Mod_Diagonal_Brace_2m")
    # Ana 45° kalin yontma payanda kirisi (2.0m boy, 0.18m x 0.16m kesit)
    mb.add_box(
        (0.0, 0.0, 0.71),
        (0.088, 0.080, 0.98),
        PAL["log_peeled_mid"],
        rot_euler=(0.0, -0.785398, 0.0),
        bevel_amt=0.010,
    )
    # Alt ve ust zimba/gecme papuclari (mortise shoes) + kavela pimleri
    mb.add_box((0.68, 0.0, 0.06), (0.12, 0.092, 0.06), PAL["stud_frame"], bevel_amt=0.006)
    mb.add_box((-0.68, 0.0, 1.36), (0.12, 0.092, 0.06), PAL["stud_frame"], bevel_amt=0.006)
    mb.add_box(
        (0.52, 0.0, 0.19),
        (0.032, 0.095, 0.032),
        PAL["iron_hardware"],
        rot_euler=(0.0, -0.785398, 0.0),
        bevel_amt=0.004,
    )
    mb.add_box(
        (-0.52, 0.0, 1.23),
        (0.032, 0.095, 0.032),
        PAL["iron_hardware"],
        rot_euler=(0.0, -0.785398, 0.0),
        bevel_amt=0.004,
    )
    mb.add_box(
        (0.0, 0.0, 0.71),
        (0.098, 0.088, 0.055),
        PAL["wood_peg"],
        rot_euler=(0.0, -0.785398, 0.0),
        bevel_amt=0.005,
    )
    return mb.finalize_object(mat, smooth_shading=False)


# ==============================================================================
# EKOL 1 — KERTIKLI KUTUK YIGMA, SUBASMAN PABUCU & CATI ASIGI (5 PARCA)
# ==============================================================================

def build_mod_notched_log_saddle_2m(mat):
    """
    Ekol 1: Gercek 0.44m capli (0.42-0.46m araligi, r=0.22m), 2.16m boyunda
    derin cift-tarafli semer kertikli (SaddleNotch) soyulmus kutuk.
    """
    mb = ModularMeshBuilder("Mod_Notched_Log_Saddle_2m")
    mb.add_notched_log(
        length=2.16,
        radius=0.22,
        notch_positions=(-0.86, 0.86),
        along_axis="X",
        center_offset=(0.0, 0.0, 0.22),
        segs=16,
    )
    for sx in (-0.86, 0.86):
        mb.add_box((sx, 0.0, 0.415), (0.028, 0.028, 0.024), PAL["wood_peg"], bevel_amt=0.005)
    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_notched_log_dovetail_2m(mat):
    """
    Ekol 1-2: 0.42m yukseklik x 0.38m kalinlikta yontulmus kutuk govdesi +
    iki ucta yamuk kesitli kirlangic kuyrugu kertik (DovetailNotch) basliklari.
    """
    mb = ModularMeshBuilder("Mod_Notched_Log_Dovetail_2m")
    # Ana yontulmus oval/karemsi kutuk govdesi (X in [-0.74..+0.74], Z in [0.0..0.42])
    mb.add_box((0.0, 0.0, 0.21), (0.74, 0.19, 0.21), PAL["log_peeled_warm"], bevel_amt=0.032)
    # Ust ve alt ince kabuk/pah izleri
    mb.add_box((0.0, -0.185, 0.21), (0.72, 0.014, 0.15), PAL["log_peeled_mid"], bevel_amt=0.006)
    mb.add_box((0.0, 0.185, 0.21), (0.72, 0.014, 0.15), PAL["log_peeled_mid"], bevel_amt=0.006)

    # Her iki ucta kirlangic kuyrugu (fan-shaped dovetail tenon) gecme basliklari
    for sign in (-1.0, 1.0):
        # Boyun daralmasi
        mb.add_box(
            (sign * 0.81, 0.0, 0.21),
            (0.08, 0.145, 0.145),
            PAL["log_notch_carve"],
            bevel_amt=0.012,
        )
        # Disa genisleyen yamuk kirlangic kuyrugu ucu (ust/alt egimli kama)
        mb.add_box(
            (sign * 0.94, 0.0, 0.255),
            (0.11, 0.165, 0.095),
            PAL["log_notch_carve"],
            rot_euler=(0.0, -sign * 0.18, 0.0),
            bevel_amt=0.010,
        )
        mb.add_box(
            (sign * 0.94, 0.0, 0.165),
            (0.11, 0.165, 0.095),
            PAL["log_notch_carve"],
            rot_euler=(0.0, sign * 0.18, 0.0),
            bevel_amt=0.010,
        )
        # Uctaki oz odun kesit halkasi ve kavela pimi
        mb.add_box(
            (sign * 1.052, 0.0, 0.21),
            (0.008, 0.135, 0.135),
            PAL["log_endgrain_outer"],
            bevel_amt=0.004,
        )
        mb.add_box(
            (sign * 1.058, 0.0, 0.21),
            (0.006, 0.075, 0.075),
            PAL["log_endgrain_heart"],
            bevel_amt=0.003,
        )
        mb.add_box((sign * 0.90, 0.0, 0.375), (0.024, 0.024, 0.020), PAL["wood_peg"], bevel_amt=0.004)
    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_chinked_log_course_2m(mat):
    """
    Ekol 1: Ust uste 2 sira 0.44m capli semer kertikli kutuk + aralarindaki derze
    sikistirilmis yosun ve kil derz dolgusu (ChinkingMossClay -> SealFactor >= 0.70).
    """
    mb = ModularMeshBuilder("Mod_Chinked_Log_Course_2m")
    # Alt kutuk (merkez Z=0.22m) ve ust kutuk (merkez Z=0.62m)
    for cz in (0.22, 0.62):
        mb.add_notched_log(
            length=2.16,
            radius=0.22,
            notch_positions=(-0.86, 0.86),
            along_axis="X",
            center_offset=(0.0, 0.0, cz),
            segs=14,
        )
    # Iki kutuk birlesim derzi boyunca (Z=0.42m) on ve arka yosun + kil siva seridi
    seg_count = 8
    seg_w = 1.68 / seg_count
    for i in range(seg_count):
        cx = -0.84 + (i + 0.5) * seg_w
        col_front = PAL["chink_clay"] if (i % 2 == 0) else PAL["chink_moss"]
        col_back = PAL["chink_moss"] if (i % 2 == 0) else PAL["chink_clay"]
        mb.add_box(
            (cx, -0.175, 0.42),
            (seg_w * 0.52, 0.036, 0.048),
            col_front,
            rot_euler=(0.08 * ((i % 3) - 1), 0.0, 0.0),
            bevel_amt=0.010,
        )
        mb.add_box(
            (cx, 0.175, 0.42),
            (seg_w * 0.52, 0.036, 0.048),
            col_back,
            rot_euler=(-0.08 * ((i % 3) - 1), 0.0, 0.0),
            bevel_amt=0.010,
        )
    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_stone_footing_pad(mat):
    """
    Ekol 1-2: Esik kutugunu (SillLog) ve dikmeleri toprak neminden yukselten
    0.68m x 0.68m x 0.36m kirma tas + yassi bazalt subasman pabucu.
    """
    mb = ModularMeshBuilder("Mod_Stone_Footing_Pad")
    # Alt sikistirilmis cakil ve harc yatagi (Z in [0.0..0.16])
    mb.add_box((0.0, 0.0, 0.08), (0.31, 0.31, 0.08), PAL["mortar_bed"], bevel_amt=0.012)
    tones = (PAL["stone_dark"], PAL["stone_warm"], PAL["stone_cool"], PAL["stone_moss_tint"])
    seed = 5
    for kx in (-0.19, 0.19):
        for ky in (-0.19, 0.19):
            mb.add_irregular_stone(
                (kx, ky, 0.11),
                (0.16, 0.16, 0.10),
                tones[seed % len(tones)],
                seed,
            )
            seed += 1
    # Ust yontulmus yassi kapak tasi + kutuk/dikme oturtma besigi (Z in [0.20..0.36])
    mb.add_box((0.0, 0.0, 0.27), (0.27, 0.27, 0.07), PAL["ashlar_limestone"], bevel_amt=0.014)
    mb.add_box((-0.20, 0.0, 0.345), (0.045, 0.24, 0.022), PAL["stone_dark"], bevel_amt=0.006)
    mb.add_box((0.20, 0.0, 0.345), (0.045, 0.24, 0.022), PAL["stone_dark"], bevel_amt=0.006)
    mb.add_box((0.0, 0.0, 0.355), (0.025, 0.025, 0.020), PAL["iron_hardware"], bevel_amt=0.004)
    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_roof_purlin_2m(mat):
    """
    Ekol 1: 2.08m uzunlukta, 0.26m capli cati asik kutugu (Roof Purlin);
    ust yuzeyde 4 mertek oturtma kertigi ve uclarda alinlik gecme zivanalari.
    """
    mb = ModularMeshBuilder("Mod_Roof_Purlin_2m")
    mb.add_notched_log(
        length=2.04,
        radius=0.13,
        notch_positions=(-0.75, -0.25, 0.25, 0.75),
        along_axis="X",
        center_offset=(0.0, 0.0, 0.13),
        segs=14,
    )
    # Uclarda alinlik duvarina gecen kare zivana dilleri + kavelalar
    for sign in (-1.0, 1.0):
        mb.add_box(
            (sign * 1.04, 0.0, 0.13),
            (0.055, 0.068, 0.068),
            PAL["log_notch_carve"],
            bevel_amt=0.008,
        )
        mb.add_box((sign * 0.75, 0.0, 0.25), (0.020, 0.020, 0.018), PAL["wood_peg"], bevel_amt=0.004)
    return mb.finalize_object(mat, smooth_shading=False)


# ==============================================================================
# EKOL 2 — AHSAP KARKAS (ZIVANA DIKME, KIRIS, WATTLE & DAUB, KERPIC) (4 PARCA)
# ==============================================================================

def build_mod_timber_post_mortise_3m(mat):
    """
    Ekol 2: 3.0m yukseklikte (0.24m x 0.24m kare kesit) yontma mese/demir agaci dikme;
    orta ve ust zivana delikleri (Mortise), kavela pimleri ve 45° payanda yuvalari.
    """
    mb = ModularMeshBuilder("Mod_Timber_Post_Mortise_3m")
    # Alt nem kesici tas/demir papuc (Z in [0.0..0.10])
    mb.add_box((0.0, 0.0, 0.05), (0.14, 0.14, 0.05), PAL["stone_dark"], bevel_amt=0.008)
    mb.add_box((0.0, 0.0, 0.115), (0.128, 0.128, 0.025), PAL["iron_hardware"], bevel_amt=0.005)
    # Ana 0.24m x 0.24m kare dikme govdesi (Z in [0.12..2.96])
    mb.add_box((0.0, 0.0, 1.54), (0.12, 0.12, 1.42), PAL["stud_frame"], bevel_amt=0.012)
    # Kose pah citasi vurgusu
    mb.add_box((0.0, 0.0, 1.54), (0.105, 0.124, 1.36), PAL["plank_warm"], bevel_amt=0.008)
    mb.add_box((0.0, 0.0, 1.54), (0.124, 0.105, 1.36), PAL["plank_mid"], bevel_amt=0.008)

    # Orta (Z=1.45m) ve Ust (Z=2.76m) zivana yuvalari (Mortise slots) + 45° payanda cepleri (Z=2.15m)
    for mz in (1.45, 2.76):
        mb.add_box((0.0, 0.0, mz), (0.128, 0.052, 0.105), PAL["bark_dark"], bevel_amt=0.004)
        mb.add_box((0.0, 0.0, mz), (0.052, 0.128, 0.105), PAL["bark_dark"], bevel_amt=0.004)
        for p_off in (-0.065, 0.065):
            mb.add_box((0.0, p_off, mz + 0.05), (0.134, 0.018, 0.018), PAL["wood_peg"], bevel_amt=0.003)
    # 45° capraz payanda (mod_diagonal_brace_2m) karsilik yuvasi
    for sign in (-1.0, 1.0):
        mb.add_box(
            (sign * 0.118, 0.0, 2.15),
            (0.022, 0.058, 0.095),
            PAL["bark_dark"],
            rot_euler=(0.0, sign * 0.785, 0.0),
            bevel_amt=0.004,
        )
    # Ust baslik zivana dili (Z in [2.94..3.00])
    mb.add_box((0.0, 0.0, 2.97), (0.065, 0.065, 0.035), PAL["log_notch_carve"], bevel_amt=0.006)
    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_timber_beam_tenon_2m(mat):
    """
    Ekol 2: 2.0m aciklik (zivana dilleriyle 2.18m), 0.22m x 0.26m kesitli
    yatay ana tasiyici kiris (MainBeam); her iki ucta gecme zivana dili (Tenon) + kavelalar.
    """
    mb = ModularMeshBuilder("Mod_Timber_Beam_Tenon_2m")
    # Ana kare/dikdortgen kiris govdesi (X in [-0.96..+0.96], Z in [0.0..0.26])
    mb.add_box((0.0, 0.0, 0.13), (0.96, 0.11, 0.13), PAL["stud_frame"], bevel_amt=0.012)
    mb.add_box((0.0, 0.0, 0.13), (0.94, 0.114, 0.105), PAL["plank_warm"], bevel_amt=0.008)
    # Her iki ucta dikme mortise yuvasina giren zivana dili (Tenon: X in [±0.96..±1.09])
    for sign in (-1.0, 1.0):
        mb.add_box(
            (sign * 1.02, 0.0, 0.13),
            (0.075, 0.050, 0.092),
            PAL["log_notch_carve"],
            bevel_amt=0.008,
        )
        # Cift mese kavela pimi
        for dz in (0.085, 0.175):
            mb.add_box(
                (sign * 1.03, 0.0, dz),
                (0.018, 0.065, 0.018),
                PAL["wood_peg"],
                bevel_amt=0.003,
            )
        # Demir kusak bilezigi
        mb.add_box(
            (sign * 0.91, 0.0, 0.13),
            (0.022, 0.118, 0.134),
            PAL["iron_hardware"],
            bevel_amt=0.004,
        )
    # Ust yuzeyde 3 adet tali doseme kirisi (joist) oturtma yuvası
    for jx in (-0.62, 0.0, 0.62):
        mb.add_box((jx, 0.0, 0.245), (0.055, 0.116, 0.022), PAL["log_notch_carve"], bevel_amt=0.004)
    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_wattle_daub_panel_2x2m(mat):
    """
    Ekol 2: 2.0m x 2.0m (0.16m kalinlik) ahsap karkas cerceve + icinde dal orgu (Wattle)
    uzerine camur/kirec siva (Daub) uygulanmis yalitimli dolgu duvar paneli.
    """
    mb = ModularMeshBuilder("Mod_Wattle_Daub_Panel_2x2m")
    # Dis ahsap karkas cercevesi + orta dikme + yatay kusak (0.16m kalinlik)
    mb.add_box((0.0, 0.0, 0.06), (1.00, 0.080, 0.06), PAL["stud_frame"], bevel_amt=0.008)
    mb.add_box((0.0, 0.0, 1.94), (1.00, 0.080, 0.06), PAL["stud_frame"], bevel_amt=0.008)
    mb.add_box((-0.94, 0.0, 1.00), (0.06, 0.080, 0.88), PAL["stud_frame"], bevel_amt=0.008)
    mb.add_box((0.94, 0.0, 1.00), (0.06, 0.080, 0.88), PAL["stud_frame"], bevel_amt=0.008)
    mb.add_box((0.0, 0.0, 1.00), (0.055, 0.078, 0.88), PAL["stud_frame"], bevel_amt=0.007)
    mb.add_box((0.0, 0.0, 1.00), (0.88, 0.074, 0.050), PAL["stud_frame"], bevel_amt=0.007)

    # Sol alt ve sag ust kose capraz karkas goguslemeleri (yarim-ahsap Tudor/Anadolu gorunumu)
    mb.add_box(
        (-0.47, 0.0, 0.53),
        (0.045, 0.072, 0.56),
        PAL["plank_dark"],
        rot_euler=(0.0, -0.785, 0.0),
        bevel_amt=0.006,
    )
    mb.add_box(
        (0.47, 0.0, 1.47),
        (0.045, 0.072, 0.56),
        PAL["plank_dark"],
        rot_euler=(0.0, -0.785, 0.0),
        bevel_amt=0.006,
    )

    # 4 karkas gozunde camur-kirec siva (Daub) panelleri
    quads = [
        (-0.47, 0.53, PAL["daub_plaster"]),
        (0.47, 0.53, PAL["daub_shade"]),
        (-0.47, 1.47, PAL["daub_shade"]),
        (0.47, 1.47, PAL["daub_plaster"]),
    ]
    for qx, qz, col in quads:
        mb.add_box((qx, 0.0, qz), (0.415, 0.048, 0.415), col, bevel_amt=0.008)

    # Sag alt gozde dokunsal gercekcilik icin kismen gorunen findik/sogut dal orgu (Wattle) cubuklari
    for wx in (0.28, 0.47, 0.66):
        mb.add_box((wx, -0.050, 0.36), (0.014, 0.010, 0.16), PAL["wattle_hazel"], bevel_amt=0.002)
    for wz in (0.26, 0.33, 0.40, 0.47):
        mb.add_box(
            (0.47, -0.052, wz),
            (0.24, 0.010, 0.013),
            PAL["log_peeled_mid"],
            rot_euler=(0.0, 0.04 if (int(wz * 100) % 2 == 0) else -0.04, 0.0),
            bevel_amt=0.002,
        )
    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_adobe_wall_2x2m(mat):
    """
    Ekol 2: 2.0m x 2.0m (0.34m kalinlik) yuksek isil kutleli kerpic tugla (Adobe Brick) duvari;
    alt tas subasman, 7 sira sasirtmali guneste kurutulmus kerpic ve ust ahsap hatil kirisi.
    """
    mb = ModularMeshBuilder("Mod_Adobe_Wall_2x2m")
    # Alt nem kesici tas temel sirasi (Z in [0.0..0.18])
    mb.add_box((0.0, 0.0, 0.09), (0.99, 0.175, 0.09), PAL["stone_dark"], bevel_amt=0.012)
    # Iceride kerpic camur harci cekirdegi (Z in [0.18..1.88])
    mb.add_box((0.0, 0.0, 1.03), (0.96, 0.145, 0.85), PAL["adobe_mortar"], bevel_amt=0.010)

    # 7 sira sasirtmali (running bond) kerpic tugla (Z in [0.18..1.86])
    rows = 7
    z0 = 0.18
    z1 = 1.86
    step_z = (z1 - z0) / rows
    brick_tones = (PAL["adobe_brick_warm"], PAL["adobe_brick_mid"], PAL["chink_clay"])
    for r in range(rows):
        cz = z0 + (r + 0.5) * step_z
        hz = step_z * 0.43
        cols = 5 if (r % 2 == 0) else 6
        step_x = 2.0 / cols
        for c in range(cols):
            x_left = -1.0 + c * step_x + 0.012
            x_right = -1.0 + (c + 1) * step_x - 0.012
            if r % 2 == 1 and (c == 0 or c == cols - 1):
                pass
            cx = 0.5 * (x_left + x_right)
            hx = 0.5 * (x_right - x_left)
            tone = brick_tones[(r * 3 + c) % len(brick_tones)]
            mb.add_box((cx, 0.0, cz), (hx, 0.168, hz), tone, bevel_amt=0.012)

    # Ust ahsap baglama hatili / duvar yastigi (Wall Plate, Z in [1.86..2.00])
    mb.add_box((0.0, 0.0, 1.93), (1.00, 0.172, 0.07), PAL["stud_frame"], bevel_amt=0.010)
    return mb.finalize_object(mat, smooth_shading=False)


# ==============================================================================
# EKOL 3 — TAS YIGMA, KIREC HARCI, KOSE KILIT TASI, LENTO & TAHFIF KEMERI (5 PARCA)
# ==============================================================================

def build_mod_rubble_stone_course_2x1m(mat):
    """
    Ekol 3: 2.0m genislik x 1.0m yukseklik x 0.44m kalinlikta sasirtmali (Running Bond)
    kirma tas (Rubble Stone) duvar sirasi + kirec harci (LimeMortar COLOR_0) derz yatagi.
    """
    mb = ModularMeshBuilder("Mod_Rubble_Stone_Course_2x1m")
    # Kirec harci ic cekirdek yatagi (Z in [0.02..0.98])
    mb.add_box((0.0, 0.0, 0.50), (0.95, 0.185, 0.48), PAL["lime_mortar_light"], bevel_amt=0.012)

    stone_tones = (
        PAL["stone_warm"],
        PAL["stone_cool"],
        PAL["stone_light"],
        PAL["stone_dark"],
        PAL["ashlar_warm"],
    )
    seed = 19
    # 3 yatay sira sasirtmali kirma tas (on ve arka yuz + kenet taslari)
    row_zs = (0.17, 0.50, 0.83)
    for r_idx, cz in enumerate(row_zs):
        count_x = 5 if (r_idx % 2 == 0) else 6
        step_x = 1.92 / count_x
        for c_idx in range(count_x):
            cx = -0.96 + (c_idx + 0.5) * step_x
            rx = step_x * 0.47
            for side_y in (-0.115, 0.115):
                mb.add_irregular_stone(
                    (cx, side_y, cz),
                    (rx, 0.115, 0.155),
                    stone_tones[seed % len(stone_tones)],
                    seed,
                )
                seed += 1
    # Ust derz yuzeyinde 4 adet enine baglama tasi (through-stones)
    for tx in (-0.72, -0.24, 0.24, 0.72):
        mb.add_box((tx, 0.0, 0.975), (0.17, 0.205, 0.025), PAL["ashlar_limestone"], bevel_amt=0.008)
    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_ashlar_quoin_corner(mat):
    """
    Ekol 3: 0.72m x 0.72m L-tabanli, 2.0m yukseklikte uzun-kisa sasirtmali (Long-and-Short Work)
    gonyeli kesme kose kilit tasi (Ashlar Quoin Corner) + kirec harci derzleri ve kenet yuvalari.
    """
    mb = ModularMeshBuilder("Mod_Ashlar_Quoin_Corner")
    courses = 6
    dz = 2.0 / courses
    tones = (PAL["ashlar_limestone"], PAL["ashlar_warm"], PAL["arch_carved"], PAL["stone_light"])

    # Kose harc omurgasi
    mb.add_box((-0.12, -0.12, 1.00), (0.20, 0.20, 0.98), PAL["lime_mortar_light"], bevel_amt=0.008)

    for i in range(courses):
        cz = (i + 0.5) * dz
        hz = dz * 0.455
        tone = tones[i % len(tones)]
        if i % 2 == 0:
            # +X kolu uzun (0.68m), +Y kolu kisa (0.44m)
            mb.add_box((0.0, -0.14, cz), (0.34, 0.22, hz), tone, bevel_amt=0.014)
            mb.add_box(
                (-0.14, 0.18, cz),
                (0.20, 0.16, hz * 0.96),
                tones[(i + 1) % len(tones)],
                bevel_amt=0.012,
            )
        else:
            # +Y kolu uzun (0.68m), +X kolu kisa (0.44m)
            mb.add_box((-0.14, 0.0, cz), (0.22, 0.34, hz), tone, bevel_amt=0.014)
            mb.add_box(
                (0.18, -0.14, cz),
                (0.16, 0.20, hz * 0.96),
                tones[(i + 1) % len(tones)],
                bevel_amt=0.012,
            )

    # Ust yuzeyde demir/kursun kenet (LeadCramp) baglanti demirleri
    mb.add_box((0.08, -0.14, 1.99), (0.09, 0.024, 0.012), PAL["iron_hardware"], bevel_amt=0.003)
    mb.add_box((-0.14, 0.08, 1.99), (0.024, 0.09, 0.012), PAL["iron_hardware"], bevel_amt=0.003)
    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_stone_lintel_2m(mat):
    """
    Ekol 3: 2.20m boy x 0.46m derinlik x 0.38m yukseklikte yekpare yontulmus tas lento
    (Stone Lintel); uc mesnet yastiklari, alt damlalik silmesi ve demir kenet pimleri.
    """
    mb = ModularMeshBuilder("Mod_Stone_Lintel_2m")
    # Ana yekpare tas lento kirisi (X in [-1.10..+1.10], Z in [0.04..0.36])
    mb.add_box((0.0, 0.0, 0.20), (1.10, 0.23, 0.16), PAL["basalt_lintel"], bevel_amt=0.016)
    # Ust yuk dagitici kesme tas sirti
    mb.add_box((0.0, 0.0, 0.35), (1.06, 0.21, 0.03), PAL["ashlar_limestone"], bevel_amt=0.010)
    # Her iki ucta sove ustune oturan genisletilmis mesnet yastiklari (X = ±0.92m)
    for sign in (-1.0, 1.0):
        mb.add_box((sign * 0.92, 0.0, 0.03), (0.18, 0.235, 0.03), PAL["arch_carved"], bevel_amt=0.008)
        mb.add_box((sign * 0.95, 0.0, 0.375), (0.065, 0.028, 0.014), PAL["iron_hardware"], bevel_amt=0.003)
    # Orta kilit kabartmasi
    mb.add_box((0.0, 0.0, 0.20), (0.14, 0.242, 0.168), PAL["arch_keystone"], bevel_amt=0.012)
    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_relieving_arch_2m(mat):
    """
    Ekol 3: 2.16m genislik x 0.46m derinlik x 0.96m yukseklikte alt tas lento +
    harcli alin dolgusu (tympanum) + 7 kama tasli (voussoir + keystone) Tahfif Kemeri.
    """
    mb = ModularMeshBuilder("Mod_Relieving_Arch_2m")
    # 1) Alt yatay lento kirisi (Z in [0.0..0.22])
    mb.add_box((0.0, 0.0, 0.11), (1.08, 0.22, 0.11), PAL["basalt_lintel"], bevel_amt=0.014)
    # 2) Lento ile kemer arasindaki geri cekilmis harcli tas alin dolgusu (Tympanum)
    mb.add_box((0.0, 0.0, 0.36), (0.78, 0.185, 0.15), PAL["lime_mortar_light"], bevel_amt=0.010)
    for tx in (-0.42, 0.0, 0.42):
        mb.add_irregular_stone((tx, 0.0, 0.38), (0.22, 0.195, 0.11), PAL["stone_warm"], int((tx + 1.0) * 10))

    # 3) Ust tahfif kemeri halkasi (7 kama tasi: -3..+3, merkezde yuksek Keystone)
    arch_cx = 0.0
    arch_cz = -0.12
    arch_r = 0.92
    tones = (PAL["ashlar_limestone"], PAL["arch_carved"], PAL["ashlar_warm"])
    for k in range(-3, 4):
        ang = k * 0.26  # radyan (-0.78 .. +0.78)
        px = arch_cx + arch_r * math.sin(ang)
        pz = arch_cz + arch_r * math.cos(ang)
        is_keystone = k == 0
        col = PAL["arch_keystone"] if is_keystone else tones[abs(k) % len(tones)]
        hx = 0.145 if is_keystone else 0.135
        hy = 0.238 if is_keystone else 0.228
        hz = 0.205 if is_keystone else 0.175
        mb.add_box(
            (px, 0.0, pz),
            (hx, hy, hz),
            col,
            rot_euler=(0.0, ang, 0.0),
            bevel_amt=0.012,
        )
    return mb.finalize_object(mat, smooth_shading=False)


def build_mod_stepped_plinth_base(mat):
    """
    Ekol 3 & Faz I.6 (TerrainPlinthSolver): 2.0m x 2.0m taban x 1.20m yukseklikte
    egimli yamaca oturan 3 kademeli agir harcli tas temel kaidesi & istinat duvari.
    """
    mb = ModularMeshBuilder("Mod_Stepped_Plinth_Base")
    # 1. Alt genis kademe (Z in [0.0..0.42], 2.04m x 2.04m)
    mb.add_box((0.0, 0.0, 0.21), (1.02, 1.02, 0.21), PAL["stone_dark"], bevel_amt=0.016)
    # 2. Orta harcli kesme/kirma tas kademesi (Z in [0.42..0.84], 1.94m x 1.94m)
    mb.add_box((0.0, 0.0, 0.63), (0.93, 0.93, 0.21), PAL["lime_mortar_light"], bevel_amt=0.010)
    tones = (PAL["ashlar_limestone"], PAL["stone_warm"], PAL["stone_cool"], PAL["ashlar_warm"])
    seed = 31
    for side in (-1.0, 1.0):
        for off in (-0.62, 0.0, 0.62):
            mb.add_box(
                (off, side * 0.85, 0.63),
                (0.29, 0.12, 0.195),
                tones[seed % len(tones)],
                bevel_amt=0.012,
            )
            seed += 1
            mb.add_box(
                (side * 0.85, off, 0.63),
                (0.12, 0.29, 0.195),
                tones[seed % len(tones)],
                bevel_amt=0.012,
            )
            seed += 1
    # Su tahliye barbakan delikleri (weep holes)
    for wx in (-0.45, 0.45):
        mb.add_box((wx, -0.975, 0.46), (0.045, 0.025, 0.065), PAL["bark_dark"], bevel_amt=0.004)
        mb.add_box((wx, 0.975, 0.46), (0.045, 0.025, 0.065), PAL["bark_dark"], bevel_amt=0.004)

    # 3. Ust kornisli platform kademesi (Z in [0.84..1.20], 2.00m x 2.00m ust tabla)
    mb.add_box((0.0, 0.0, 1.00), (0.96, 0.96, 0.16), PAL["ashlar_warm"], bevel_amt=0.014)
    mb.add_box((0.0, 0.0, 1.16), (1.00, 1.00, 0.04), PAL["ashlar_limestone"], bevel_amt=0.010)
    return mb.finalize_object(mat, smooth_shading=False)


# ==============================================================================
# GLB IHRACI (0 DOKU, UV-FREE COLOR_0) & 4 ACILI 512x512 CONTACT SHEET RENDER
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
        if "BLENDER_EEVEE_NEXT" in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items.keys()
        else "BLENDER_EEVEE"
    )
    scene.render.resolution_x = 512
    scene.render.resolution_y = 512
    scene.render.image_settings.file_format = "PNG"

    world = bpy.data.worlds.new("ModularPreviewWorld")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs["Color"].default_value = (0.14, 0.17, 0.21, 1.0)
        bg.inputs["Strength"].default_value = 1.05

    sun_data = bpy.data.lights.new(name="SunKey", type="SUN")
    sun_data.energy = 3.2
    sun_obj = bpy.data.objects.new(name="SunKey", object_data=sun_data)
    sun_obj.rotation_euler = Euler((0.72, 0.25, 0.62), "XYZ")
    bpy.context.collection.objects.link(sun_obj)

    fill_data = bpy.data.lights.new(name="FillSun", type="SUN")
    fill_data.energy = 1.35
    fill_obj = bpy.data.objects.new(name="FillSun", object_data=fill_data)
    fill_obj.rotation_euler = Euler((1.05, -0.45, -2.25), "XYZ")
    bpy.context.collection.objects.link(fill_obj)

    cam_data = bpy.data.cameras.new("ModularPreviewCam")
    cam_obj = bpy.data.objects.new("ModularPreviewCam", cam_data)
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


def render_piece_4_angle_contact_sheet(scene, cam_obj, cam_data, target_obj, all_piece_objs, asset_id):
    for o in all_piece_objs:
        o.hide_render = o != target_obj

    bbox = [target_obj.matrix_world @ Vector(corner) for corner in target_obj.bound_box]
    min_v = Vector((min(p.x for p in bbox), min(p.y for p in bbox), min(p.z for p in bbox)))
    max_v = Vector((max(p.x for p in bbox), max(p.y for p in bbox), max(p.z for p in bbox)))
    center = 0.5 * (min_v + max_v)
    span = max(0.85, (max_v - min_v).length)
    dist = span * 1.42

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


def render_kit_lineup_contact_sheet(scene, cam_obj, cam_data, ordered_objs):
    """Tum 12 moduler parcayi acik izgarada dizip 4 acili genel bakis karti uretir."""
    for idx, obj in enumerate(ordered_objs):
        obj.hide_render = False
        col = idx % 4
        row = idx // 4
        obj.location = Vector(((col - 1.5) * 2.85, (row - 1.0) * 2.85, 0.0))

    center = Vector((0.0, 0.0, 0.95))
    views = [
        ("front", Vector((0.0, -11.5, 4.2)), center, 40.0),
        ("side", Vector((-11.8, -1.5, 4.2)), center, 40.0),
        ("top", Vector((0.0, -2.2, 13.5)), center, 40.0),
        ("iso", Vector((-8.8, -9.2, 6.8)), center, 40.0),
    ]
    temp_paths = []
    for tag, pos, tgt, lens in views:
        cam_obj.location = pos
        cam_data.lens = lens
        direction = (tgt - pos).normalized()
        cam_obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
        out_p = os.path.join(OUT_PREVIEW_DIR, f"_tmp_kit_overview_{tag}.png")
        scene.render.filepath = out_p
        bpy.ops.render.render(write_still=True)
        temp_paths.append(out_p)

    out_sheet = os.path.join(OUT_PREVIEW_DIR, "modular_kit_overview_contact_sheet.png")
    _compose_2x2_contact_sheet(temp_paths, out_sheet, "Sheet_ModularKitOverview")
    return out_sheet


def main():
    import sys

    only_preview_id = None
    skip_previews = False
    if "--" in sys.argv:
        extra = sys.argv[sys.argv.index("--") + 1 :]
        if "--skip-previews" in extra:
            skip_previews = True
        if len(extra) >= 2 and extra[0] == "--only-preview":
            only_preview_id = extra[1]

    clear_scene()
    mat = create_preview_vertex_color_material()

    kit_specs = [
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
        # Ekol 1 (5 Parca)
        ("mod_notched_log_saddle_2m", build_mod_notched_log_saddle_2m),
        ("mod_notched_log_dovetail_2m", build_mod_notched_log_dovetail_2m),
        ("mod_chinked_log_course_2m", build_mod_chinked_log_course_2m),
        ("mod_stone_footing_pad", build_mod_stone_footing_pad),
        ("mod_roof_purlin_2m", build_mod_roof_purlin_2m),
        # Ekol 2 (4 Parca)
        ("mod_timber_post_mortise_3m", build_mod_timber_post_mortise_3m),
        ("mod_timber_beam_tenon_2m", build_mod_timber_beam_tenon_2m),
        ("mod_wattle_daub_panel_2x2m", build_mod_wattle_daub_panel_2x2m),
        ("mod_adobe_wall_2x2m", build_mod_adobe_wall_2x2m),
        # Ekol 3 (5 Parca)
        ("mod_rubble_stone_course_2x1m", build_mod_rubble_stone_course_2x1m),
        ("mod_ashlar_quoin_corner", build_mod_ashlar_quoin_corner),
        ("mod_stone_lintel_2m", build_mod_stone_lintel_2m),
        ("mod_relieving_arch_2m", build_mod_relieving_arch_2m),
        ("mod_stepped_plinth_base", build_mod_stepped_plinth_base),
    ]

    built_objs = []
    for asset_id, builder_fn in kit_specs:
        obj = builder_fn(mat)
        out_glb = os.path.join(OUT_MODULAR_DIR, f"{asset_id}.glb")
        export_single_piece_glb(obj, out_glb)
        tris = len(obj.data.polygons)
        size_kb = os.path.getsize(out_glb) / 1024.0
        print(f"[ModularKitBuilder] Exported {asset_id}.glb ({tris} tris, {size_kb:.1f} KB)")
        built_objs.append((asset_id, obj))

    if skip_previews:
        return

    scene, cam_obj, cam_data = setup_studio_render_rig()
    all_objs = [o for _, o in built_objs]
    for asset_id, obj in built_objs:
        if only_preview_id and asset_id != only_preview_id:
            continue
        sheet_path = render_piece_4_angle_contact_sheet(
            scene, cam_obj, cam_data, obj, all_objs, asset_id
        )
        print(f"[ModularKitBuilder] Preview: {sheet_path}")

    overview_sheet = render_kit_lineup_contact_sheet(scene, cam_obj, cam_data, all_objs)
    print(f"[ModularKitBuilder] Kit Overview Preview: {overview_sheet}")


if __name__ == "__main__":
    main()
