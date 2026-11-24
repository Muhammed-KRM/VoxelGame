"""
Stratocraft Otonom 3B Fabrika — 9 Ekol Genisletilmis Mimari, Gecici Destek, Betonarme/Celik ve
Istasyon Seti Ureticisi (Blender 4.2).

Kullanim (0 gomulu doku, UV-free COLOR_0, < 16.0 MB VRAM):
    blender --background --python Tools/AutonomousAssetFactory/generators/expanded_9schools_kit_builder.py

Uretilen 18 Yeni 9-Ekol .glb Parca (Assets/Art/Generated/Structures_Modular/*.glb):
    1.  mod_grooved_log_post_3m.glb         — Oluklu dikey kutuk dikmesi (LogPostAndFill, 3.0m)
    2.  mod_temp_wood_brace_2m.glb          — Gecici 45° kurulum payandasi (TemporaryWoodBrace, 2.0m)
    3.  mod_wooden_arch_centering_2m.glb    — Ahsap kemer kalibi + kama takozlari (2.0m)
    4.  mod_sliding_earth_formwork_2m.glb   — Gergili kayar toprak kalibi (SlidingEarthFormwork, 2.0m)
    5.  mod_cob_curved_wall_2m.glb          — Kalipsiz kavisli samanlı Cob camur duvar (2.0x2.0m)
    6.  mod_rammed_earth_wall_2x2m.glb      — Katmanli sikistirilmis toprak duvar (2.0x2.0m)
    7.  mod_english_bond_brick_2x2m.glb     — Ingiliz bagi pismis tugla duvar + demir gergi (2.0x2.0m)
    8.  mod_timber_ring_beam_2m.glb         — Cift kirisli deprem/baglama ahsap hatil kusagi (2.0m)
    9.  mod_platform_stud_frame_2x2m.glb    — Hafif platform dikme duvar iskeleti (2.0x2.0m)
    10. mod_osb_sheathing_panel_2x2m.glb    — OSB / kontrplak perde duvar levhasi (Shear Wall, 2.0x2.0m)
    11. mod_rebar_foundation_cage_2m.glb    — Grobeton + hasir celik + L-filizli temel demiri (2.0x2.0m)
    12. mod_concrete_column_formwork_3m.glb — Kusakli ve payandali betonarme kolon kalibi (3.0m)
    13. mod_telescopic_shoring_2x3m.glb     — Teleskopik celik dikme ormani + tabliye kalibi (2.0x2.0x3.0m)
    14. mod_steel_h_column_3m.glb           — Ankraj bulonlu H/I-profil celik kolon (3.0m)
    15. mod_steel_x_brace_2x2m.glb          — Guse plakali celik X-capraz + gergi halati (2.0x2.0m)
    16. mod_metal_deck_slab_2x2m.glb        — Oluklu trapez sac + kompozit beton tabliye (2.0x2.0m)
    17. mod_station_sawhorse_kiln_bundle.glb — Bicki Sehpasi + Kirec/Tugla/Klinker Firini + Cam Ocagi
    18. mod_station_trough_chisel_steel.glb  — Harc/Beton Teknesi + Tasci Tezgahi + Celik Hadde Tezgahi
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

# 9-Ekol Ahsap, Toprak/Cob, Pismis Tugla, OSB, Betonarme, Yapısal Celik ve Atolye Paleti (sRGB -> COLOR_0)
# R kanali asla 0, 3 veya 11 secilmez (StratocraftUberShader maskeleriyle %100 ayrik).
PAL = {
    # Ahsap, kutuk, kalas ve OSB tonlari
    "log_peeled_warm": (196, 148, 92, 248),
    "log_peeled_mid": (176, 128, 76, 244),
    "log_groove_dark": (112, 76, 44, 228),
    "bark_dark": (78, 54, 38, 222),
    "timber_oak_warm": (168, 118, 70, 246),
    "timber_oak_mid": (148, 102, 58, 242),
    "timber_oak_dark": (118, 78, 44, 234),
    "stud_spruce_light": (206, 166, 108, 248),
    "stud_spruce_mid": (188, 146, 92, 244),
    "osb_flake_gold": (198, 152, 88, 246),
    "osb_flake_amber": (178, 130, 72, 244),
    "osb_flake_tan": (212, 170, 106, 248),
    "osb_edge_seal": (72, 118, 96, 240),
    "wood_peg": (190, 146, 90, 246),
    "hemp_rope": (188, 162, 112, 242),
    "strike_wedge_ochre": (214, 132, 54, 248),
    # Tas, harc, tugla, kerpic, cob ve sikistirilmis toprak tonlari
    "stone_plinth_dark": (98, 100, 106, 236),
    "stone_plinth_warm": (146, 138, 126, 244),
    "ashlar_limestone": (190, 184, 172, 248),
    "dpc_slate_membrane": (52, 56, 62, 238),
    "lime_mortar_light": (208, 202, 190, 238),
    "cob_earth_warm": (186, 142, 96, 246),
    "cob_earth_mid": (168, 126, 82, 244),
    "cob_earth_shade": (148, 108, 68, 238),
    "cob_straw_fleck": (218, 184, 112, 250),
    "rammed_layer_ochre": (194, 138, 84, 246),
    "rammed_layer_sienna": (172, 112, 68, 244),
    "rammed_layer_terracotta": (184, 104, 66, 246),
    "rammed_layer_sand": (206, 164, 114, 248),
    "rammed_layer_umber": (144, 98, 62, 240),
    "brick_stretcher_red": (178, 84, 60, 246),
    "brick_header_dark": (142, 66, 48, 242),
    "brick_kiln_warm": (194, 98, 68, 248),
    # Betonarme, kalip, donati ve yapisal celik tonlari
    "grobeton_lean": (148, 150, 146, 240),
    "concrete_fresh_wet": (132, 138, 142, 244),
    "concrete_cured_light": (174, 176, 172, 248),
    "formwork_phenol_brown": (96, 58, 42, 242),
    "formwork_h20_yellow": (218, 168, 48, 248),
    "shoring_prop_blue": (58, 96, 136, 244),
    "shoring_prop_galv": (168, 176, 184, 246),
    "rebar_ribbed_steel": (76, 84, 92, 244),
    "rebar_rust_tint": (116, 78, 56, 240),
    "steel_hbeam_bluegrey": (82, 94, 108, 246),
    "steel_hbeam_dark": (58, 66, 76, 242),
    "steel_primer_oxide": (138, 64, 52, 244),
    "galv_metal_deck": (184, 192, 200, 248),
    "galv_deck_rib_shade": (152, 160, 170, 242),
    "wrought_iron": (62, 66, 74, 240),
    "brass_collar": (184, 146, 76, 248),
    # Atolye ates ve cam tonlari
    "kiln_ember_glow": (232, 102, 40, 252),
    "molten_glass_gold": (242, 168, 58, 252),
    "arch_glass_aqua": (158, 204, 216, 224),
    "leather_bellows": (114, 72, 48, 240),
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
    mat = bpy.data.materials.new(name="M_Expanded9SchoolsVertexColorPreview")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    vcol = nt.nodes.new("ShaderNodeVertexColor")
    vcol.layer_name = "Col"
    bsdf.inputs["Roughness"].default_value = 0.72
    bsdf.inputs["Specular IOR Level"].default_value = 0.25
    nt.links.new(vcol.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


class ExpandedKitMeshBuilder:
    """
    UV-free COLOR_0 9-Ekol mimari parca bmesh insacisi.
    Koordinat eslemesi (export_yup=True):
      Blender +Z -> glTF/Unity +Y (Yukari, taban Z=0.0m)
      Blender -Y -> glTF/Unity +Z (Ileri)
      Blender +X -> glTF +X / Unity -X (Yatay genislik)
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
            t_h = max(0.0, min(1.0, (cz - z_ao_origin) / max(0.25, z_ao_span)))
            shade = (1.0 - z_ao_strength * 0.65) + z_ao_strength * t_h
            if nrm.z < -0.45:
                shade *= 0.84
            elif nrm.z > 0.55:
                shade *= 1.05
            ao = max(0.55, min(1.0, lin[3] * (0.88 + 0.12 * t_h)))
            col = (
                max(0.025, min(1.0, lin[0] * shade)),
                max(0.015, min(1.0, lin[1] * shade)),
                max(0.015, min(1.0, lin[2] * shade)),
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

    def add_cylinder(
        self,
        center,
        radius,
        height,
        rgba_u8,
        segs=12,
        rot_euler=(0.0, 0.0, 0.0),
    ):
        prev_faces = set(self.bm.faces)
        rot_m = Euler(rot_euler, "XYZ").to_matrix().to_4x4()
        xform = Matrix.Translation(Vector(center)) @ rot_m
        bmesh.ops.create_cone(
            self.bm,
            cap_ends=True,
            cap_tris=False,
            segments=segs,
            radius1=radius,
            radius2=radius,
            depth=height,
            matrix=xform,
        )
        new_faces = [f for f in self.bm.faces if f not in prev_faces and f.is_valid]
        bmesh.ops.recalc_face_normals(self.bm, faces=new_faces)
        self.paint_faces(new_faces, rgba_u8)
        return new_faces

    def finalize_object(self, mat, smooth_shading=False):
        bmesh.ops.triangulate(self.bm, faces=self.bm.faces[:])
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces[:])

        # BottomCenter_Zero garantisi: X ve Y ekseninde tam merkezle, tabani Z=0.0m'ye oturt
        if self.bm.verts:
            xs = [v.co.x for v in self.bm.verts]
            ys = [v.co.y for v in self.bm.verts]
            zs = [v.co.z for v in self.bm.verts]
            shift = Vector((-0.5 * (min(xs) + max(xs)), -0.5 * (min(ys) + max(ys)), -min(zs)))
            for v in self.bm.verts:
                v.co += shift

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
# 18 YENI 9-EKOL MIMARI VE ISTASYON PARCA URETICILERI
# ==============================================================================


def build_mod_grooved_log_post_3m(mat):
    """
    1. mod_grooved_log_post_3m.glb — Ekol 2 (LogPostAndFill):
    3.0m yukseklikte, iki yaninda (-X ve +X) yatay dolgu kutuklerinin kayarak oturdugu
    derin dikey kanallar (U-oluk), tas/demir nem papucu ve kavela delikleri bulunan dikme.
    """
    mb = ExpandedKitMeshBuilder("Mod_Grooved_Log_Post_3m")
    # Alt tas + demir nem kesici papuc (Z in [0.0..0.14])
    mb.add_box((0.0, 0.0, 0.05), (0.19, 0.19, 0.05), PAL["stone_plinth_dark"], bevel_amt=0.010)
    mb.add_box((0.0, 0.0, 0.12), (0.175, 0.175, 0.025), PAL["wrought_iron"], bevel_amt=0.005)

    # Merkez tasiyici omurga + on/arka (-Y/+Y) yuvarlatilmis kutuk yanaklari (-X/+X'te derin U-oluk birakir)
    mb.add_box((0.0, 0.0, 1.51), (0.075, 0.165, 1.37), PAL["log_groove_dark"], bevel_amt=0.008)
    for sy in (-0.118, 0.118):
        mb.add_box((0.0, sy, 1.51), (0.168, 0.052, 1.37), PAL["log_peeled_warm"], bevel_amt=0.018)
        mb.add_box((0.0, sy * 1.32, 1.51), (0.135, 0.024, 1.36), PAL["log_peeled_mid"], bevel_amt=0.010)

    # Alt, orta ve ust demir kelepce kusaklari + kanal kavela pimleri
    for kz in (0.24, 1.50, 2.76):
        mb.add_box((0.0, 0.0, kz), (0.176, 0.176, 0.028), PAL["wrought_iron"], bevel_amt=0.004)
    for pz in (0.55, 1.05, 1.95, 2.45):
        for sx in (-0.13, 0.13):
            mb.add_box((sx, 0.0, pz), (0.022, 0.185, 0.022), PAL["wood_peg"], bevel_amt=0.003)

    # Ust baslik kirisi gecme zivanasi (Tenon, Z in [2.88..3.00])
    mb.add_box((0.0, 0.0, 2.94), (0.085, 0.085, 0.06), PAL["stud_spruce_light"], bevel_amt=0.008)
    return mb.finalize_object(mat)


def build_mod_temp_wood_brace_2m(mat):
    """
    2. mod_temp_wood_brace_2m.glb — Gecici 45° Kurulum Payandasi (TemporaryWoodBrace):
    Zemine cakili ahsap kazik (WoodenStake), 45° diyagonal destek kalasi, ust dikme kavrama
    cenesi, halat sargilari ve [Shift+E] %100 sokum takoz isaretcisi.
    """
    mb = ExpandedKitMeshBuilder("Mod_Temp_Wood_Brace_2m")
    # Alt zemin sabitleme kazigi (WoodenStake) + taban takozu (X=+0.62, Z in [0.0..0.38])
    mb.add_box((0.64, 0.0, 0.19), (0.048, 0.055, 0.19), PAL["timber_oak_dark"], bevel_amt=0.006)
    mb.add_box(
        (0.56, 0.0, 0.10),
        (0.14, 0.085, 0.045),
        PAL["stud_spruce_mid"],
        rot_euler=(0.0, 0.20, 0.0),
        bevel_amt=0.005,
    )

    # Ana 45° gecici destek kalasi + yan takviye citalari (2.0m diyagonal)
    mb.add_box(
        (0.0, 0.0, 0.78),
        (0.062, 0.048, 0.96),
        PAL["stud_spruce_light"],
        rot_euler=(0.0, -0.785398, 0.0),
        bevel_amt=0.007,
    )
    for sy in (-0.056, 0.056):
        mb.add_box(
            (0.0, sy, 0.78),
            (0.045, 0.014, 0.72),
            PAL["timber_oak_warm"],
            rot_euler=(0.0, -0.785398, 0.0),
            bevel_amt=0.004,
        )

    # Ust dikme/duvar kavrama cenesi (X=-0.62, Z in [1.30..1.56])
    mb.add_box((-0.62, 0.0, 1.43), (0.095, 0.092, 0.13), PAL["timber_oak_mid"], bevel_amt=0.006)
    # Sokulebilir kama takozlari (strike_wedge_ochre) ve kenevir halat bogumlari
    for pos in ((0.48, 0.28), (0.0, 0.78), (-0.48, 1.28)):
        mb.add_box(
            (pos[0], 0.0, pos[1]),
            (0.082, 0.078, 0.042),
            PAL["hemp_rope"],
            rot_euler=(0.0, -0.785398, 0.0),
            bevel_amt=0.004,
        )
    mb.add_box((0.58, 0.0, 0.22), (0.075, 0.095, 0.032), PAL["strike_wedge_ochre"], bevel_amt=0.004)
    mb.add_box((-0.56, 0.0, 1.38), (0.075, 0.095, 0.032), PAL["strike_wedge_ochre"], bevel_amt=0.004)
    return mb.finalize_object(mat)


def build_mod_wooden_arch_centering_2m(mat):
    """
    3. mod_wooden_arch_centering_2m.glb — Ahsap Kemer Kalibi (WoodenArchCentering):
    2.0m aciklikli yarim daire kemer kalibi; alt cift kama takozlari (Folding Wedges),
    gergi kirisi, radyal yelpaze dikmeleri ve ustte voussoir tasiyan ahsap lateler.
    """
    mb = ExpandedKitMeshBuilder("Mod_Wooden_Arch_Centering_2m")
    # Her iki mesnette [Shift+E] kalip sokumu icin cift kama takozu (Folding Wedges, Z in [0.0..0.12])
    for sx in (-0.86, 0.86):
        mb.add_box((sx, 0.0, 0.035), (0.13, 0.21, 0.035), PAL["timber_oak_dark"], bevel_amt=0.005)
        mb.add_box((sx, 0.0, 0.090), (0.12, 0.19, 0.028), PAL["strike_wedge_ochre"], bevel_amt=0.004)

    # Alt yatay gergi kirisi (Tie Beam, Z in [0.12..0.24]) + orta kral dikmesi (King Post)
    for sy in (-0.15, 0.15):
        mb.add_box((0.0, sy, 0.18), (0.98, 0.038, 0.06), PAL["timber_oak_warm"], bevel_amt=0.006)
    mb.add_box((0.0, 0.0, 0.62), (0.055, 0.16, 0.40), PAL["timber_oak_mid"], bevel_amt=0.006)

    # Yarim daire on/arka kemer kaburgalari + radyal yelpaze payandalari + ust lagging citalari
    segs = 9
    r_arch = 0.88
    z_center = 0.20
    for k in range(segs):
        t = -1.0 + (2.0 * (k + 0.5)) / segs
        ang = t * 1.38
        px = r_arch * math.sin(ang)
        pz = z_center + r_arch * math.cos(ang)
        for sy in (-0.15, 0.15):
            mb.add_box(
                (px, sy, pz),
                (0.155, 0.036, 0.058),
                PAL["stud_spruce_mid"] if (k % 2 == 0) else PAL["timber_oak_warm"],
                rot_euler=(0.0, ang, 0.0),
                bevel_amt=0.005,
            )
        # Ust kemer tasi (Voussoir) oturtma lateleri (Lagging slats)
        lx = (r_arch + 0.07) * math.sin(ang)
        lz = z_center + (r_arch + 0.07) * math.cos(ang)
        mb.add_box(
            (lx, 0.0, lz),
            (0.115, 0.23, 0.022),
            PAL["stud_spruce_light"],
            rot_euler=(0.0, ang, 0.0),
            bevel_amt=0.004,
        )
        # Radyal ic destek kollari
        if k in (1, 2, 6, 7):
            mb.add_box(
                (px * 0.55, 0.0, z_center + 0.42 * math.cos(ang)),
                (0.038, 0.11, 0.34),
                PAL["timber_oak_dark"],
                rot_euler=(0.0, ang * 0.72, 0.0),
                bevel_amt=0.004,
            )
    return mb.finalize_object(mat)


def build_mod_sliding_earth_formwork_2m(mat):
    """
    4. mod_sliding_earth_formwork_2m.glb — Kayar Toprak Kalibi (SlidingEarthFormwork):
    2.0m uzunluk x 0.92m yukseklikte iki paralel OSB/kalas kalip panosu, 4 cift dikey kusak,
    demir saplamalar (Through-Bolts + kelebek somun) ve icinde tokmaklanan toprak katmani.
    """
    mb = ExpandedKitMeshBuilder("Mod_Sliding_Earth_Formwork_2m")
    # Kalip icinde yarim doldurulup tokmaklanmis sikistirilmis toprak cekirdegi (Z in [0.02..0.58])
    mb.add_box((0.0, 0.0, 0.16), (0.94, 0.18, 0.14), PAL["rammed_layer_sienna"], bevel_amt=0.008)
    mb.add_box((0.0, 0.0, 0.44), (0.94, 0.18, 0.14), PAL["rammed_layer_ochre"], bevel_amt=0.008)

    # On ve Arka (Y = ±0.205m) OSB + kalas kayar kalip yanak panolari (Z in [0.0..0.82])
    for sy in (-1.0, 1.0):
        mb.add_box((0.0, sy * 0.202, 0.41), (0.99, 0.022, 0.40), PAL["osb_flake_gold"], bevel_amt=0.005)
        for rz in (0.14, 0.41, 0.68):
            mb.add_box(
                (0.0, sy * 0.232, rz),
                (1.00, 0.018, 0.055),
                PAL["timber_oak_warm"],
                bevel_amt=0.004,
            )

    # 4 cift dikey gergi dikmesi (Soldiers) + enine demir saplamalar ve kelebek somunlar
    for sx in (-0.82, -0.28, 0.28, 0.82):
        for sy in (-1.0, 1.0):
            mb.add_box((sx, sy * 0.265, 0.46), (0.042, 0.032, 0.46), PAL["timber_oak_dark"], bevel_amt=0.005)
        for bz in (0.14, 0.72):
            mb.add_box((sx, 0.0, bz), (0.016, 0.32, 0.016), PAL["wrought_iron"], bevel_amt=0.002)
            for sy in (-1.0, 1.0):
                mb.add_box(
                    (sx, sy * 0.308, bz),
                    (0.042, 0.014, 0.028),
                    PAL["strike_wedge_ochre"],
                    bevel_amt=0.003,
                )
    return mb.finalize_object(mat)


def build_mod_cob_curved_wall_2m(mat):
    """
    5. mod_cob_curved_wall_2m.glb — Kalipsiz Kavisli Cob Duvar (CobCurvedWall):
    >= 40 cm yuksek tas subasman uzerinde 4 organik katman halinde (<= 45 cm/katman)
    elle yogrulmus kavisli samanli kil-camur duvar ve saman lifi pariltilari.
    """
    mb = ExpandedKitMeshBuilder("Mod_Cob_Curved_Wall_2m")
    # 1. Yuksek su gecirmez tas subasman (Z in [0.0..0.42], kesintisiz kavisli omurga + taslar)
    segs_x = 9
    step_x = 2.0 / segs_x
    stone_cols = (PAL["stone_plinth_dark"], PAL["stone_plinth_warm"], PAL["ashlar_limestone"])
    for i in range(segs_x):
        cx = -0.96 + (i + 0.5) * (1.92 / segs_x)
        curve_y = 0.095 * math.sin((cx / 1.0) * math.pi * 0.82)
        yaw = -0.22 * math.cos((cx / 1.0) * math.pi * 0.82)
        mb.add_box(
            (cx, curve_y, 0.21),
            (step_x * 0.62, 0.245, 0.21),
            stone_cols[i % len(stone_cols)],
            rot_euler=(0.0, 0.0, yaw),
            bevel_amt=0.018,
        )

    # 2. 4 adet organik kavisli yekpare Cob camur katmani (Z in [0.42..2.00], yukari dogru hafif daralan)
    lifts = [
        (0.615, 0.192, 0.236, PAL["cob_earth_shade"]),
        (1.005, 0.192, 0.224, PAL["cob_earth_mid"]),
        (1.395, 0.192, 0.212, PAL["cob_earth_warm"]),
        (1.785, 0.192, 0.200, PAL["cob_earth_mid"]),
    ]
    for l_idx, (cz, hz, hy, col) in enumerate(lifts):
        stagger_count = 9 if (l_idx % 2 == 0) else 8
        s_step = 1.92 / stagger_count
        for i in range(stagger_count):
            cx = -0.96 + (i + 0.5) * s_step
            curve_y = 0.095 * math.sin((cx / 1.0) * math.pi * 0.82)
            yaw = -0.22 * math.cos((cx / 1.0) * math.pi * 0.82)
            tone = PAL["cob_earth_warm"] if ((i + l_idx) % 2 == 0) else col
            mb.add_box(
                (cx, curve_y, cz),
                (s_step * 0.64, hy, hz),
                tone,
                rot_euler=(0.0, 0.0, yaw),
                bevel_amt=0.024,
            )
            # Yuzeyde gorunen altin sarisi saman lifi topaklari (Straw Flecks)
            if (i + l_idx) % 2 == 0:
                for sy in (-1.0, 1.0):
                    mb.add_box(
                        (cx, curve_y + sy * (hy + 0.006), cz + 0.02),
                        (0.082, 0.008, 0.024),
                        PAL["cob_straw_fleck"],
                        rot_euler=(0.0, 0.16 * sy, yaw),
                        bevel_amt=0.003,
                    )
    return mb.finalize_object(mat)


def build_mod_rammed_earth_wall_2x2m(mat):
    """
    6. mod_rammed_earth_wall_2x2m.glb — Sikistirilmis Toprak Duvar (RammedEarthWall):
    2.0m x 2.0m x 0.44m boyutunda; su gecirmez tas kaide + DPC nem bandi, 9 yatay
    tortul toprak tabakasi, kalip saplama delikleri ve ust ahsap hatil yastigi.
    """
    mb = ExpandedKitMeshBuilder("Mod_Rammed_Earth_Wall_2x2m")
    # Alt su gecirmez kesme tas kaide (Z in [0.0..0.26]) + koyu arduvaz DPC nem bariyeri (Z in [0.26..0.31])
    mb.add_box((0.0, 0.0, 0.13), (1.00, 0.23, 0.13), PAL["stone_plinth_dark"], bevel_amt=0.012)
    mb.add_box((0.0, 0.0, 0.285), (1.005, 0.235, 0.024), PAL["dpc_slate_membrane"], bevel_amt=0.005)

    # 9 yatay sikistirilmis tortul toprak katmani (Z in [0.31..1.86], ust uste binmeyen net derzli)
    layer_tones = (
        PAL["rammed_layer_ochre"],
        PAL["rammed_layer_sienna"],
        PAL["rammed_layer_sand"],
        PAL["rammed_layer_terracotta"],
        PAL["rammed_layer_umber"],
        PAL["rammed_layer_ochre"],
        PAL["rammed_layer_sand"],
        PAL["rammed_layer_sienna"],
        PAL["rammed_layer_terracotta"],
    )
    z0 = 0.31
    z1 = 1.86
    dz = (z1 - z0) / len(layer_tones)
    for idx, col in enumerate(layer_tones):
        cz = z0 + (idx + 0.5) * dz
        mb.add_box((0.0, 0.0, cz), (0.995, 0.22, dz * 0.478), col, bevel_amt=0.006)

    # Kayar kaliptan kalan 2x3 saplama deligi tapalari (Formwork Tie-Rod Holes)
    for tx in (-0.65, 0.0, 0.65):
        for tz in (0.62, 1.48):
            mb.add_box((tx, 0.0, tz), (0.032, 0.232, 0.032), PAL["dpc_slate_membrane"], bevel_amt=0.004)

    # Ust ahsap hatil baglama kusagi (Z in [1.86..2.00])
    mb.add_box((0.0, 0.0, 1.93), (1.00, 0.225, 0.068), PAL["timber_oak_dark"], bevel_amt=0.008)
    return mb.finalize_object(mat)


def build_mod_english_bond_brick_2x2m(mat):
    """
    7. mod_english_bond_brick_2x2m.glb — Ingiliz Bagi Pismis Tugla Duvar (EnglishBondBrickWall):
    2.0m x 2.0m x 0.36m tasiyici tugla duvar; DPC nem bandi, bir sira boyuna (Stretcher)
    ve bir sira enine (Header) dizilmis Ingiliz bagi pismis tuglalar + FloorJoistIronStrap gergileri.
    """
    mb = ExpandedKitMeshBuilder("Mod_English_Bond_Brick_2x2m")
    # Alt tas subasman + DPC nem bandi (Z in [0.0..0.12])
    mb.add_box((0.0, 0.0, 0.045), (1.00, 0.185, 0.045), PAL["stone_plinth_dark"], bevel_amt=0.008)
    mb.add_box((0.0, 0.0, 0.105), (1.002, 0.188, 0.016), PAL["dpc_slate_membrane"], bevel_amt=0.004)

    # 10 sira Ingiliz bagi (Stretcher boyuna / Header enine) + katmanli harc yatagi
    rows = 10
    z0 = 0.12
    z1 = 1.92
    step_z = (z1 - z0) / rows
    for r in range(rows):
        cz = z0 + (r + 0.5) * step_z
        # Her sira altinda ince kirec harci yatagi
        mb.add_box(
            (0.0, 0.0, cz - step_z * 0.44),
            (0.985, 0.168, step_z * 0.075),
            PAL["lime_mortar_light"],
            bevel_amt=0.002,
        )
        hz = step_z * 0.39
        is_header_row = (r % 2) == 1
        cols = 9 if is_header_row else 5
        step_x = 2.0 / cols
        for c in range(cols):
            cx = -1.0 + (c + 0.5) * step_x
            hx = step_x * 0.455
            col = (
                PAL["brick_header_dark"]
                if is_header_row
                else (PAL["brick_stretcher_red"] if ((r + c) % 2 == 0) else PAL["brick_kiln_warm"])
            )
            mb.add_box((cx, 0.0, cz + step_z * 0.03), (hx, 0.178, hz), col, bevel_amt=0.006)

    # Ust tugla harpuşta / hatil sirasi + 3 adet dovme demir doseme gergi ankraji (FloorJoistIronStrap)
    mb.add_box((0.0, 0.0, 1.96), (1.00, 0.185, 0.038), PAL["brick_header_dark"], bevel_amt=0.007)
    for ax in (-0.68, 0.0, 0.68):
        mb.add_box((ax, 0.0, 1.72), (0.045, 0.198, 0.12), PAL["wrought_iron"], bevel_amt=0.004)
        mb.add_box((ax, 0.0, 1.72), (0.11, 0.194, 0.028), PAL["wrought_iron"], bevel_amt=0.003)
    return mb.finalize_object(mat)


def build_mod_timber_ring_beam_2m(mat):
    """
    8. mod_timber_ring_beam_2m.glb — Ahsap Hatil Kusagi (TimberRingBeam):
    Tas, tugla, kerpic ve sikistirilmis toprak duvarlari yatayda birbirine kilitleyen
    2.0m boyunda merdiven tipi cift paralel ahsap hatil kirisi + enine baglar ve demir kenetler.
    """
    mb = ExpandedKitMeshBuilder("Mod_Timber_Ring_Beam_2m")
    # Dis ve ic duvar yuzunu takip eden 2 paralel boyuna hatil kirisi (Y = ±0.145m, Z in [0.0..0.20])
    for sy in (-0.145, 0.145):
        mb.add_box((0.0, sy, 0.10), (0.98, 0.065, 0.10), PAL["timber_oak_warm"], bevel_amt=0.008)
        # Uclarda yarim-bindirme (half-lap) gecme dilleri
        for sx in (-0.98, 0.98):
            mb.add_box((sx, sy, 0.05), (0.04, 0.062, 0.05), PAL["stud_spruce_light"], bevel_amt=0.005)

    # 5 adet enine zivanali baglama basamagi (Cross-Rungs) + mese kavelalar + demir kusaklar
    for rx in (-0.80, -0.40, 0.0, 0.40, 0.80):
        mb.add_box((rx, 0.0, 0.10), (0.052, 0.215, 0.055), PAL["timber_oak_dark"], bevel_amt=0.006)
        for sy in (-0.145, 0.145):
            mb.add_box((rx, sy, 0.205), (0.020, 0.020, 0.015), PAL["wood_peg"], bevel_amt=0.003)
    for sx in (-0.90, 0.90):
        mb.add_box((sx, 0.0, 0.10), (0.026, 0.218, 0.106), PAL["wrought_iron"], bevel_amt=0.004)
    return mb.finalize_object(mat)


def build_mod_platform_stud_frame_2x2m(mat):
    """
    9. mod_platform_stud_frame_2x2m.glb — Platform Dikme Duvar Iskeleti (PlatformStudWall):
    2.0m x 2.0m x 0.16m hafif ahsap platform cerceve: alt taban plakasi (Sole Plate),
    cift ust baslik plakasi (Double Top Plate), 40 cm aralikli 6 dikme, yangin takozlari ve capraz cita.
    """
    mb = ExpandedKitMeshBuilder("Mod_Platform_Stud_Frame_2x2m")
    # Alt taban plakasi (Sole Plate) ve Cift Ust Plaka (Double Top Plate, Z in [1.88..2.00])
    mb.add_box((0.0, 0.0, 0.028), (1.00, 0.075, 0.028), PAL["timber_oak_dark"], bevel_amt=0.005)
    mb.add_box((0.0, 0.0, 1.910), (1.00, 0.075, 0.028), PAL["stud_spruce_mid"], bevel_amt=0.005)
    mb.add_box((0.0, 0.0, 1.970), (1.00, 0.075, 0.030), PAL["stud_spruce_light"], bevel_amt=0.005)

    # 6 adet dikey platform dikmesi (Studs: X = -0.96, -0.58, -0.19, +0.19, +0.58, +0.96)
    stud_xs = (-0.955, -0.575, -0.192, 0.192, 0.575, 0.955)
    for idx, sx in enumerate(stud_xs):
        col = PAL["stud_spruce_light"] if (idx % 2 == 0) else PAL["stud_spruce_mid"]
        mb.add_box((sx, 0.0, 0.968), (0.028, 0.072, 0.915), col, bevel_amt=0.005)
        # Galvanizli delikli birlesim plakalari (Truss/Stud Nail Plates)
        for pz in (0.08, 1.85):
            mb.add_box((sx, 0.0, pz), (0.038, 0.078, 0.042), PAL["shoring_prop_galv"], bevel_amt=0.002)

    # Orta kot sasirtmali yatay yangin/burkulma takozlari (Fire-Blocking Noggings)
    for i in range(len(stud_xs) - 1):
        mx = 0.5 * (stud_xs[i] + stud_xs[i + 1])
        hw = 0.5 * (stud_xs[i + 1] - stud_xs[i]) - 0.028
        nz = 0.96 if (i % 2 == 0) else 1.04
        mb.add_box((mx, 0.0, nz), (hw, 0.068, 0.026), PAL["timber_oak_warm"], bevel_amt=0.004)

    # Gomme diyagonal gecici ruzgar citasi (Let-in Wind Brace Batten)
    mb.add_box(
        (0.0, -0.068, 0.98),
        (0.032, 0.012, 1.24),
        PAL["timber_oak_dark"],
        rot_euler=(0.0, -0.785398, 0.0),
        bevel_amt=0.003,
    )
    return mb.finalize_object(mat)


def build_mod_osb_sheathing_panel_2x2m(mat):
    """
    10. mod_osb_sheathing_panel_2x2m.glb — OSB / Kontrplak Perde Levhasi (PlywoodSheathingPanel):
    2.0m x 2.0m platform dikme iskeleti uzerine sasirtmali cakilmis 4 adet yapisal OSB/kontrplak
    perde duvar levhasi (Shear Wall Diaphragm), yonga mozaigi, yesil kenar muhru ve civi izgarasi.
    """
    mb = ExpandedKitMeshBuilder("Mod_Osb_Sheathing_Panel_2x2m")
    # Arka yuzde (+Y) platform dikme karkasi
    mb.add_box((0.0, 0.022, 0.03), (1.00, 0.062, 0.03), PAL["timber_oak_dark"], bevel_amt=0.005)
    mb.add_box((0.0, 0.022, 1.97), (1.00, 0.062, 0.03), PAL["stud_spruce_mid"], bevel_amt=0.005)
    for sx in (-0.955, -0.48, 0.0, 0.48, 0.955):
        mb.add_box((sx, 0.022, 1.00), (0.028, 0.060, 0.94), PAL["stud_spruce_light"], bevel_amt=0.004)
    mb.add_box((0.0, 0.022, 1.00), (0.93, 0.056, 0.026), PAL["timber_oak_warm"], bevel_amt=0.004)

    # On yuzde (-Y) 4 parca sasirtmali OSB perde levhasi + yesil nem sızdırmaz derz seritleri
    mb.add_box((0.0, -0.048, 1.00), (0.996, 0.012, 0.996), PAL["osb_edge_seal"], bevel_amt=0.003)
    panels = [
        (-0.50, 0.50, 0.488, 0.488, PAL["osb_flake_gold"]),
        (0.50, 0.50, 0.488, 0.488, PAL["osb_flake_amber"]),
        (-0.30, 1.50, 0.688, 0.488, PAL["osb_flake_tan"]),
        (0.70, 1.50, 0.288, 0.488, PAL["osb_flake_gold"]),
    ]
    flake_cols = (PAL["osb_flake_tan"], PAL["osb_flake_amber"], PAL["osb_flake_gold"], PAL["timber_oak_warm"])
    for idx, (px, pz, hx, hz, pcol) in enumerate(panels):
        mb.add_box((px, -0.056, pz), (hx, 0.016, hz), pcol, bevel_amt=0.004)
        # OSB preslenmis yonga dokusu vurgusu ve civi dikis izleri
        for fx in (-0.55, 0.0, 0.55):
            for fz in (-0.55, 0.55):
                mb.add_box(
                    (px + fx * hx, -0.070, pz + fz * hz),
                    (hx * 0.24, 0.005, hz * 0.22),
                    flake_cols[(idx + int((fx + 1) * 2 + (fz + 1))) % len(flake_cols)],
                    rot_euler=(0.0, 0.15 * fx, 0.0),
                    bevel_amt=0.002,
                )
    for sx in (-0.955, 0.0, 0.955):
        for sz in (0.20, 0.60, 1.00, 1.40, 1.80):
            mb.add_box((sx, -0.074, sz), (0.012, 0.004, 0.012), PAL["wrought_iron"], bevel_amt=0.001)
    return mb.finalize_object(mat)


def build_mod_rebar_foundation_cage_2m(mat):
    """
    11. mod_rebar_foundation_cage_2m.glb — L-Filizli Temel Demiri (RebarFoundationCage):
    2.0m x 2.0m grobeton koruma tabakasi, paspayi takozlari, cift katli nervurlu celik
    hasir donati izgarasi ve merkezde L-kivrimli 4 adet kolon filizi + etriyeler (H=0.95m).
    """
    mb = ExpandedKitMeshBuilder("Mod_Rebar_Foundation_Cage_2m")
    # 1. Alt Grobeton tesviye tabakasi (Z in [0.0..0.08]) + DPC membran + beton paspayi takozlari
    mb.add_box((0.0, 0.0, 0.04), (1.00, 1.00, 0.04), PAL["grobeton_lean"], bevel_amt=0.008)
    mb.add_box((0.0, 0.0, 0.088), (0.96, 0.96, 0.010), PAL["dpc_slate_membrane"], bevel_amt=0.003)
    for sx in (-0.64, 0.64):
        for sy in (-0.64, 0.64):
            mb.add_box((sx, sy, 0.115), (0.06, 0.06, 0.025), PAL["concrete_cured_light"], bevel_amt=0.004)

    # 2. Alt (Z=0.14m) ve Ust (Z=0.30m) ortogonal nervurlu hasir temel demiri izgarasi (5x5 cubuk)
    bar_coords = (-0.80, -0.40, 0.0, 0.40, 0.80)
    for rz, col in ((0.14, PAL["rebar_ribbed_steel"]), (0.30, PAL["rebar_rust_tint"])):
        for c in bar_coords:
            mb.add_box((0.0, c, rz), (0.88, 0.014, 0.014), col, bevel_amt=0.002)
            mb.add_box((c, 0.0, rz + 0.022), (0.014, 0.88, 0.014), col, bevel_amt=0.002)
    # Kenar U-kanca sehpa demirleri
    for cx in (-0.80, 0.0, 0.80):
        for cy in (-0.80, 0.80):
            mb.add_box((cx, cy, 0.22), (0.014, 0.014, 0.09), PAL["rebar_ribbed_steel"], bevel_amt=0.002)

    # 3. Merkezde L-kivrimli 4 adet dikey kolon filizi (L-Filiz: Z in [0.14..0.95]) + 3 kare etriye
    for fx in (-0.16, 0.16):
        for fy in (-0.16, 0.16):
            # Yatay L-ankraj ayagi
            mb.add_box(
                (fx * 1.65, fy * 1.65, 0.155),
                (0.14, 0.016, 0.016),
                PAL["rebar_ribbed_steel"],
                rot_euler=(0.0, 0.0, 0.785 if (fx * fy < 0) else -0.785),
                bevel_amt=0.003,
            )
            # Dikey filiz cubugu
            mb.add_box((fx, fy, 0.55), (0.018, 0.018, 0.40), PAL["rebar_ribbed_steel"], bevel_amt=0.003)
    for ez in (0.28, 0.56, 0.84):
        for s in (-0.16, 0.16):
            mb.add_box((0.0, s, ez), (0.18, 0.012, 0.012), PAL["rebar_rust_tint"], bevel_amt=0.002)
            mb.add_box((s, 0.0, ez), (0.012, 0.18, 0.012), PAL["rebar_rust_tint"], bevel_amt=0.002)
    return mb.finalize_object(mat)


def build_mod_concrete_column_formwork_3m(mat):
    """
    12. mod_concrete_column_formwork_3m.glb — Betonarme Kolon Kalibi (ConcreteColumnFormwork):
    3.0m yukseklikte 4 yanli fenol kapli kontrplak kolon kalibi, 6 sira celik/ahsap kusak
    kelepcesi, 4 yonde 45° payanda dikmeleri ve ustten filizleri cikan taze beton cekirdek.
    """
    mb = ExpandedKitMeshBuilder("Mod_Concrete_Column_Formwork_3m")
    # Alt grobeton kolon topugu (Kicker, Z in [0.0..0.08]) + ic taze beton sutun (Z in [0.08..2.78])
    mb.add_box((0.0, 0.0, 0.04), (0.48, 0.48, 0.04), PAL["grobeton_lean"], bevel_amt=0.006)
    mb.add_box((0.0, 0.0, 1.43), (0.21, 0.21, 1.35), PAL["concrete_fresh_wet"], bevel_amt=0.006)

    # 4 yanli koyu kahve fenol kapli kontrplak kalip panolari + H20 dikey destek sirtlari
    for s in (-1.0, 1.0):
        mb.add_box(
            (0.0, s * 0.228, 1.44), (0.245, 0.018, 1.36), PAL["formwork_phenol_brown"], bevel_amt=0.004
        )
        mb.add_box(
            (s * 0.228, 0.0, 1.44), (0.018, 0.245, 1.36), PAL["formwork_phenol_brown"], bevel_amt=0.004
        )
        for off in (-0.15, 0.15):
            mb.add_box(
                (off, s * 0.265, 1.44), (0.028, 0.024, 1.35), PAL["formwork_h20_yellow"], bevel_amt=0.004
            )
            mb.add_box(
                (s * 0.265, off, 1.44), (0.024, 0.028, 1.35), PAL["formwork_h20_yellow"], bevel_amt=0.004
            )

    # Hidrostatik taze beton basincini tutan 6 yatay celik kusak kelepcesi (Column Yokes)
    for yz in (0.24, 0.72, 1.20, 1.68, 2.16, 2.64):
        for s in (-0.295, 0.295):
            mb.add_box((0.0, s, yz), (0.34, 0.022, 0.026), PAL["shoring_prop_blue"], bevel_amt=0.003)
            mb.add_box((s, 0.0, yz + 0.032), (0.022, 0.34, 0.026), PAL["shoring_prop_blue"], bevel_amt=0.003)

    # 4 yanda 45° ayarli celik kolon payandalari (Push-Pull Raking Props, Z in [0.06..1.35])
    for sx, sy, rx, ry in (
        (0.36, 0.0, 0.0, -0.34),
        (-0.36, 0.0, 0.0, 0.34),
        (0.0, 0.36, 0.34, 0.0),
        (0.0, -0.36, -0.34, 0.0),
    ):
        mb.add_box(
            (sx, sy, 0.72),
            (0.022, 0.022, 0.68),
            PAL["shoring_prop_galv"],
            rot_euler=(rx, ry, 0.0),
            bevel_amt=0.003,
        )

    # Ustten (Z in [2.78..3.00]) cikan 4 adet üst kat kolon filizi + etriye
    for fx in (-0.14, 0.14):
        for fy in (-0.14, 0.14):
            mb.add_box((fx, fy, 2.89), (0.018, 0.018, 0.11), PAL["rebar_ribbed_steel"], bevel_amt=0.003)
    mb.add_box((0.0, 0.0, 2.92), (0.155, 0.155, 0.012), PAL["rebar_rust_tint"], bevel_amt=0.002)
    return mb.finalize_object(mat)


def build_mod_telescopic_shoring_2x3m(mat):
    """
    13. mod_telescopic_shoring_2x3m.glb — Teleskopik Dikme + Tabliye Kalibi (TelescopicShoringProp):
    2.0m x 2.0m x 3.0m betonarme tavan/kiris dikme ormani: 4 adet ayar bilezikli teleskopik
    celik dikme, capraz gergiler, sari H20 ahsap kirisler ve ust kontrplak tabliye kalibi.
    """
    mb = ExpandedKitMeshBuilder("Mod_Telescopic_Shoring_2x3m")
    # 4 kosede (X = ±0.76, Y = ±0.76) teleskopik celik dikmeler (Acrow Props)
    for sx in (-0.76, 0.76):
        for sy in (-0.76, 0.76):
            # Alt ahsap yastik + celik taban plakasi
            mb.add_box((sx, sy, 0.025), (0.14, 0.14, 0.025), PAL["timber_oak_dark"], bevel_amt=0.004)
            mb.add_box((sx, sy, 0.060), (0.095, 0.095, 0.012), PAL["shoring_prop_blue"], bevel_amt=0.003)
            # Dis mavi celik boru (Z in [0.07..1.52]) + ayar somunu bilezigi + G-pimi (Z=1.52m)
            mb.add_box((sx, sy, 0.80), (0.036, 0.036, 0.73), PAL["shoring_prop_blue"], bevel_amt=0.006)
            mb.add_box((sx, sy, 1.52), (0.056, 0.056, 0.048), PAL["strike_wedge_ochre"], bevel_amt=0.005)
            mb.add_box((sx, sy, 1.52), (0.095, 0.016, 0.016), PAL["wrought_iron"], bevel_amt=0.002)
            # Ic galvanizli teleskopik boru (Z in [1.52..2.54]) + U-catal baslik (Fork Head)
            mb.add_box((sx, sy, 2.03), (0.028, 0.028, 0.51), PAL["shoring_prop_galv"], bevel_amt=0.005)
            mb.add_box((sx, sy, 2.58), (0.085, 0.085, 0.045), PAL["shoring_prop_blue"], bevel_amt=0.004)

    # Dikmeler arasi yatay ve capraz burkulma onleyici iskele gergileri
    for s in (-0.76, 0.76):
        mb.add_box((0.0, s, 1.15), (0.76, 0.020, 0.020), PAL["shoring_prop_galv"], bevel_amt=0.003)
        mb.add_box((s, 0.0, 1.15), (0.020, 0.76, 0.020), PAL["shoring_prop_galv"], bevel_amt=0.003)

    # Ust sari H20 birincil ve ikincil kalip kirisleri (Z in [2.60..2.88])
    for sy in (-0.76, 0.76):
        mb.add_box((0.0, sy, 2.68), (0.98, 0.045, 0.075), PAL["formwork_h20_yellow"], bevel_amt=0.005)
    for jx in (-0.82, -0.28, 0.28, 0.82):
        mb.add_box((jx, 0.0, 2.81), (0.040, 0.98, 0.055), PAL["formwork_h20_yellow"], bevel_amt=0.005)

    # En ust 2.0m x 2.0m fenol kontrplak tabliye kalibi + kenar alin tahtalari (Z in [2.87..3.00])
    mb.add_box((0.0, 0.0, 2.895), (1.00, 1.00, 0.025), PAL["formwork_phenol_brown"], bevel_amt=0.005)
    for s in (-0.975, 0.975):
        mb.add_box((0.0, s, 2.96), (1.00, 0.025, 0.040), PAL["osb_flake_gold"], bevel_amt=0.004)
    return mb.finalize_object(mat)


def build_mod_steel_h_column_3m(mat):
    """
    14. mod_steel_h_column_3m.glb — H/I-Profil Celik Kolon (SteelHBeamColumn):
    3.0m yukseklikte beton kaide uzerine 4 ankraj bulonu ve denge somunuyla (SteelAnchorBoltSet)
    bagli, takviye bayrak plakali (Stiffeners) ve bulonlu kiris kulakli agir H-profil celik kolon.
    """
    mb = ExpandedKitMeshBuilder("Mod_Steel_H_Column_3m")
    # 1. Alt grobeton ankraj kaidesi (Z in [0.0..0.14]) + celik taban plakasi (Baseplate, Z in [0.14..0.20])
    mb.add_box((0.0, 0.0, 0.07), (0.36, 0.36, 0.07), PAL["concrete_cured_light"], bevel_amt=0.008)
    mb.add_box((0.0, 0.0, 0.17), (0.31, 0.31, 0.030), PAL["steel_hbeam_dark"], bevel_amt=0.005)
    # 4 kose ankraj bulonu + alt/ust seviyeleme somunlari + bayrak guse plakalari
    for bx in (-0.23, 0.23):
        for by in (-0.23, 0.23):
            mb.add_box((bx, by, 0.21), (0.026, 0.026, 0.065), PAL["shoring_prop_galv"], bevel_amt=0.003)
            mb.add_box((bx, by, 0.225), (0.042, 0.042, 0.022), PAL["brass_collar"], bevel_amt=0.003)
    for s in (-0.17, 0.17):
        mb.add_box((s, 0.0, 0.29), (0.014, 0.18, 0.095), PAL["steel_primer_oxide"], bevel_amt=0.003)

    # 2. Ana H-profil celik kolon govdesi (HEB 360: X = ±0.17m flanslar + merkez govde, Z in [0.20..2.94])
    for fx in (-0.165, 0.165):
        mb.add_box((fx, 0.0, 1.57), (0.024, 0.185, 1.37), PAL["steel_hbeam_bluegrey"], bevel_amt=0.005)
    mb.add_box((0.0, 0.0, 1.57), (0.145, 0.018, 1.37), PAL["steel_hbeam_dark"], bevel_amt=0.004)

    # Orta ve ust yatay govde burkulma rijitlik plakalari (Web Stiffeners)
    for sz in (1.05, 1.95, 2.74):
        for sy in (-0.095, 0.095):
            mb.add_box((0.0, sy, sz), (0.145, 0.075, 0.016), PAL["steel_primer_oxide"], bevel_amt=0.003)

    # 3. Ust kat kiris baglanti konsol kulaklari (Shear Tabs, Z=2.62m) + bulonlar + tepe plakasi
    for sx in (-1.0, 1.0):
        mb.add_box((sx * 0.255, 0.0, 2.62), (0.075, 0.018, 0.145), PAL["steel_primer_oxide"], bevel_amt=0.004)
        for bz in (2.53, 2.62, 2.71):
            mb.add_box((sx * 0.275, 0.0, bz), (0.018, 0.036, 0.018), PAL["brass_collar"], bevel_amt=0.002)
    mb.add_box((0.0, 0.0, 2.97), (0.22, 0.22, 0.030), PAL["steel_hbeam_bluegrey"], bevel_amt=0.005)
    return mb.finalize_object(mat)


def build_mod_steel_x_brace_2x2m(mat):
    """
    15. mod_steel_x_brace_2x2m.glb — Celik X-Capraz & Gergi Halati (SteelXBrace):
    2.0m x 2.0m endustriyel ruzgar/deprem caprazlama modulu: 4 kose bulonlu guse plakasi,
    merkez sekizgen dugum plakasi, cift kosebent celik X-capraz ve gerdirmeli celik halat.
    """
    mb = ExpandedKitMeshBuilder("Mod_Steel_X_Brace_2x2m")
    # Alt ve ust ince montaj cerceve profilleri + 4 kose guse plakasi (Gusset Plates)
    for cz in (0.04, 1.96):
        mb.add_box((0.0, 0.0, cz), (0.98, 0.045, 0.04), PAL["steel_hbeam_dark"], bevel_amt=0.004)
    for sx in (-0.82, 0.82):
        for sz in (0.18, 1.82):
            mb.add_box((sx, 0.0, sz), (0.18, 0.026, 0.18), PAL["steel_primer_oxide"], bevel_amt=0.005)
            for bx in (-0.07, 0.07):
                for bz in (-0.07, 0.07):
                    mb.add_box(
                        (sx + bx, 0.0, sz + bz),
                        (0.020, 0.042, 0.020),
                        PAL["brass_collar"],
                        bevel_amt=0.002,
                    )

    # +45° ve -45° celik U/L profil X-capraz kollari
    for sign in (-1.0, 1.0):
        mb.add_box(
            (0.0, sign * 0.022, 1.00),
            (0.055, 0.022, 1.22),
            PAL["steel_hbeam_bluegrey"],
            rot_euler=(0.0, sign * 0.785398, 0.0),
            bevel_amt=0.005,
        )

    # Merkez bulonlu dugum guse plakasi + yan gergi halati ve gerdirme (SteelGuyWireBrace)
    mb.add_box((0.0, 0.0, 1.00), (0.22, 0.042, 0.22), PAL["steel_primer_oxide"], bevel_amt=0.006)
    for bx in (-0.11, 0.11):
        for bz in (-0.11, 0.11):
            mb.add_box((bx, 0.0, 1.00 + bz), (0.022, 0.058, 0.022), PAL["brass_collar"], bevel_amt=0.003)

    # Gerdirmeli (Turnbuckle) gecici/yardimci celik halat (Y=-0.075m)
    mb.add_box(
        (0.0, -0.075, 1.00),
        (0.016, 0.016, 1.24),
        PAL["shoring_prop_galv"],
        rot_euler=(0.0, -0.785398, 0.0),
        bevel_amt=0.002,
    )
    mb.add_box(
        (0.28, -0.075, 0.72),
        (0.038, 0.038, 0.14),
        PAL["strike_wedge_ochre"],
        rot_euler=(0.0, -0.785398, 0.0),
        bevel_amt=0.004,
    )
    return mb.finalize_object(mat)


def build_mod_metal_deck_slab_2x2m(mat):
    """
    16. mod_metal_deck_slab_2x2m.glb — Oluklu Trapez Sac & Kompozit Doseme (CorrugatedMetalDeck):
    2.0m x 2.0m x 0.26m dikmesiz kompozit doseme: 2 alt celik I-kiris, 6 oluklu galvanizli
    trapez sac (Metal Deck), kesme kamalari, hasir celik ve ust perdahli betonarme tabliye.
    """
    mb = ExpandedKitMeshBuilder("Mod_Metal_Deck_Slab_2x2m")
    # 1. Alt tasiyici celik I-profil tali doseme kirisleri (X = ±0.66m, Z in [0.0..0.10])
    for jx in (-0.66, 0.66):
        mb.add_box((jx, 0.0, 0.014), (0.085, 0.99, 0.014), PAL["steel_hbeam_bluegrey"], bevel_amt=0.003)
        mb.add_box((jx, 0.0, 0.050), (0.016, 0.99, 0.035), PAL["steel_hbeam_dark"], bevel_amt=0.003)
        mb.add_box((jx, 0.0, 0.088), (0.085, 0.99, 0.014), PAL["steel_hbeam_bluegrey"], bevel_amt=0.003)

    # 2. 6 hadveli galvanizli oluklu trapez sac (Corrugated Trapezoidal Deck, Z in [0.10..0.165])
    ribs = 6
    step_x = 2.0 / ribs
    for r in range(ribs):
        cx = -1.0 + (r + 0.5) * step_x
        # Alt vadi + ust tepe olugu (on taraf Y=-0.98'e kadar acikta gorunur)
        mb.add_box(
            (cx, 0.0, 0.112), (step_x * 0.26, 0.995, 0.012), PAL["galv_deck_rib_shade"], bevel_amt=0.003
        )
        mb.add_box((cx, 0.0, 0.152), (step_x * 0.34, 0.995, 0.016), PAL["galv_metal_deck"], bevel_amt=0.004)
        # Kesme kamalari (Nelson Shear Studs)
        for sy in (-0.82, 0.0, 0.82):
            mb.add_box((cx, sy, 0.182), (0.018, 0.018, 0.028), PAL["brass_collar"], bevel_amt=0.002)

    # 3. On kenarda (-Y) kesit detayi olarak gorunen hasir celik + perdahli beton tabliye (Z in [0.16..0.26])
    for rx in (-0.75, -0.25, 0.25, 0.75):
        mb.add_box((rx, -0.86, 0.185), (0.012, 0.12, 0.012), PAL["rebar_ribbed_steel"], bevel_amt=0.002)
    mb.add_box((0.0, 0.08, 0.212), (0.985, 0.90, 0.048), PAL["concrete_cured_light"], bevel_amt=0.008)
    # Cevre celik beton tutucu kenar kornisi (Perimeter Slab Stop Angle)
    for s in (-0.985, 0.985):
        mb.add_box((s, 0.0, 0.195), (0.016, 0.995, 0.065), PAL["steel_primer_oxide"], bevel_amt=0.003)
    return mb.finalize_object(mat)


def build_mod_station_sawhorse_kiln_bundle(mat):
    """
    17. mod_station_sawhorse_kiln_bundle.glb — Istasyon Paketi 1 (2.0m x 2.0m x 1.92m):
      - Bicki & Marangoz Sehpasi (SawhorseBench): X-ayakli mese sehpa, ustunde yarim kesilmis
        oluklu kutuk, demir el testeresi ve yaninda OSB / kemer kalibi parcalari.
      - Kirec, Tugla & Cimento Firini (MasonryKiln): Tas kaideli, tugla kubbeli, 1450°C kor
        hazneli ve bacali firin (icinde pisen tugla ve beyaz kirectasi/klinker).
      - Cam Ergitme Ocagi (GlassPotFurnace): Ates tuglali pota ocagi, erimis altin cam potasi,
        cift kollu deri koruk ve soguyan pencere cami levhalari.
    """
    mb = ExpandedKitMeshBuilder("Mod_Station_Sawhorse_Kiln_Bundle")
    # Alt tas ve ahsap atolye taban platformu (Z in [0.0..0.10], 2.0m x 2.0m)
    mb.add_box((0.0, 0.0, 0.05), (1.00, 1.00, 0.05), PAL["stone_plinth_dark"], bevel_amt=0.010)

    # A) SOL TARAF (X in [-0.92..-0.12]): Bicki & Marangoz Sehpasi (SawhorseBench)
    for sy in (-0.58, 0.42):
        for tilt in (-0.38, 0.38):
            mb.add_box(
                (-0.54, sy, 0.46),
                (0.042, 0.042, 0.42),
                PAL["timber_oak_dark"],
                rot_euler=(0.0, tilt, 0.0),
                bevel_amt=0.005,
            )
    mb.add_box((-0.54, -0.08, 0.74), (0.075, 0.64, 0.065), PAL["timber_oak_warm"], bevel_amt=0.006)
    # Sehpa ustunde bickiya yatirilmis soyulmus kutuk + sapli celik testere
    mb.add_box((-0.54, -0.05, 0.92), (0.13, 0.72, 0.13), PAL["log_peeled_warm"], bevel_amt=0.025)
    mb.add_box(
        (-0.42, -0.05, 1.02),
        (0.26, 0.008, 0.075),
        PAL["shoring_prop_galv"],
        rot_euler=(0.0, -0.22, 0.0),
        bevel_amt=0.002,
    )
    mb.add_box((-0.18, -0.05, 1.08), (0.055, 0.028, 0.085), PAL["strike_wedge_ochre"], bevel_amt=0.004)
    # Sehpa yaninda dikili OSB levha ve kalas destesi
    mb.add_box(
        (-0.86, 0.10, 0.54),
        (0.035, 0.48, 0.44),
        PAL["osb_flake_gold"],
        rot_euler=(0.0, -0.14, 0.0),
        bevel_amt=0.005,
    )

    # B) SAG ARKA (X in [0.05..0.92], Y in [0.02..0.92]): Kirec, Tugla & Cimento Firini (MasonryKiln)
    mb.add_box((0.46, 0.46, 0.48), (0.44, 0.44, 0.38), PAL["stone_plinth_warm"], bevel_amt=0.014)
    mb.add_box((0.46, 0.46, 1.12), (0.38, 0.38, 0.32), PAL["brick_stretcher_red"], bevel_amt=0.016)
    mb.add_box((0.46, 0.46, 1.56), (0.28, 0.28, 0.18), PAL["brick_header_dark"], bevel_amt=0.014)
    mb.add_box((0.46, 0.46, 1.82), (0.16, 0.16, 0.12), PAL["wrought_iron"], bevel_amt=0.008)
    # Demir kusaklar + on kor ates agzi + kirectasi/tugla sarj agzi
    for kz in (0.62, 1.18, 1.52):
        mb.add_box((0.46, 0.46, kz), (0.40, 0.40, 0.024), PAL["wrought_iron"], bevel_amt=0.004)
    mb.add_box((0.46, 0.02, 0.36), (0.18, 0.05, 0.20), PAL["kiln_ember_glow"], bevel_amt=0.008)
    mb.add_box((0.46, 0.07, 1.12), (0.15, 0.04, 0.14), PAL["lime_mortar_light"], bevel_amt=0.006)

    # C) SAG ON (X in [0.08..0.90], Y in [-0.90..-0.12]): Cam Ergitme Ocagi (GlassPotFurnace) + Koruk + Cam
    mb.add_box((0.38, -0.54, 0.42), (0.30, 0.28, 0.32), PAL["brick_kiln_warm"], bevel_amt=0.012)
    mb.add_box((0.38, -0.54, 0.80), (0.22, 0.22, 0.12), PAL["brick_header_dark"], bevel_amt=0.010)
    # Erimis cam potasi (Glory Hole) + deri koruk + soguyan pencere cami
    mb.add_box((0.38, -0.25, 0.48), (0.12, 0.04, 0.12), PAL["molten_glass_gold"], bevel_amt=0.005)
    mb.add_box(
        (0.78, -0.54, 0.32),
        (0.14, 0.20, 0.09),
        PAL["leather_bellows"],
        rot_euler=(0.0, 0.24, 0.0),
        bevel_amt=0.006,
    )
    mb.add_box((0.12, -0.82, 0.44), (0.20, 0.022, 0.28), PAL["arch_glass_aqua"], bevel_amt=0.004)
    return mb.finalize_object(mat)


def build_mod_station_trough_chisel_steel(mat):
    """
    18. mod_station_trough_chisel_steel.glb — Istasyon Paketi 2 (2.0m x 2.0m x 1.56m):
      - Harc, Camur & Beton Teknesi (MortarAndConcreteTrough): Demir cemberli karisim teknesi,
        icinde taze harc/beton, Cob camur toplari, kurek (ToolShovel) ve tokmak (ToolEarthTamper).
      - Tasci Yontma Tezgahi (MasonsChiselTable): Kalin kutuk ayakli bazalt tabla, ustunde
        yontulan kemer kilit tasi (Voussoir), tasci tokmagi, keski ve sakul (ToolPlumbAndLevel).
      - Celik Hadde & Donati Tezgahi (SteelRebarBench): Ates tuglali celik tezgah, etriye bukme
        pimleri, bukulmus rebar kafesi, H-profil celik stok ve tork anahtari (ToolTorqueWrench).
    """
    mb = ExpandedKitMeshBuilder("Mod_Station_Trough_Chisel_Steel")
    # Alt atolye doseme izgarasi (Z in [0.0..0.08], 2.0m x 2.0m)
    mb.add_box((0.0, 0.0, 0.04), (1.00, 1.00, 0.04), PAL["stone_plinth_dark"], bevel_amt=0.010)

    # A) SOL ON (X in [-0.92..-0.08], Y in [-0.90..-0.08]): Harc, Camur & Beton Teknesi
    mb.add_box((-0.50, -0.52, 0.28), (0.42, 0.32, 0.20), PAL["timber_oak_dark"], bevel_amt=0.008)
    mb.add_box((-0.50, -0.52, 0.28), (0.43, 0.33, 0.035), PAL["wrought_iron"], bevel_amt=0.004)
    mb.add_box((-0.50, -0.52, 0.44), (0.36, 0.26, 0.05), PAL["concrete_fresh_wet"], bevel_amt=0.006)
    # Tekne icinde CobMudBall topaklari + yaninda dikili Kurek (ToolShovel) ve Tokmak (ToolEarthTamper)
    for cx in (-0.64, -0.46):
        mb.add_box((cx, -0.48, 0.51), (0.075, 0.075, 0.055), PAL["cob_straw_fleck"], bevel_amt=0.014)
    mb.add_box(
        (-0.14, -0.48, 0.64),
        (0.025, 0.025, 0.52),
        PAL["stud_spruce_light"],
        rot_euler=(0.0, 0.22, 0.0),
        bevel_amt=0.004,
    )
    mb.add_box((-0.04, -0.48, 0.22), (0.09, 0.11, 0.020), PAL["wrought_iron"], bevel_amt=0.003)
    mb.add_box((-0.86, -0.20, 0.22), (0.10, 0.10, 0.14), PAL["timber_oak_warm"], bevel_amt=0.008)
    mb.add_box((-0.86, -0.20, 0.62), (0.026, 0.026, 0.34), PAL["stud_spruce_mid"], bevel_amt=0.004)

    # B) SOL ARKA (X in [-0.92..-0.06], Y in [0.08..0.90]): Tasci Yontma Tezgahi (MasonsChiselTable)
    for lx in (-0.82, -0.18):
        for ly in (0.22, 0.78):
            mb.add_box((lx, ly, 0.40), (0.085, 0.085, 0.32), PAL["timber_oak_dark"], bevel_amt=0.008)
    mb.add_box((-0.50, 0.50, 0.78), (0.44, 0.38, 0.08), PAL["stone_plinth_warm"], bevel_amt=0.010)
    # Tabla ustunde yari yontulmus mermer kemer tasi (Voussoir), tasci cekici, keski ve sakul A-cercevesi
    mb.add_box(
        (-0.56, 0.50, 0.98),
        (0.22, 0.18, 0.14),
        PAL["ashlar_limestone"],
        rot_euler=(0.0, 0.14, 0.0),
        bevel_amt=0.012,
    )
    mb.add_box((-0.24, 0.42, 0.91), (0.08, 0.04, 0.022), PAL["shoring_prop_galv"], bevel_amt=0.003)
    mb.add_box((-0.24, 0.62, 1.02), (0.14, 0.03, 0.16), PAL["strike_wedge_ochre"], bevel_amt=0.005)

    # C) SAG TARAF (X in [0.08..0.92], Y in [-0.88..0.88]): Celik Hadde & Donati Tezgahi (SteelRebarBench)
    mb.add_box((0.52, 0.0, 0.44), (0.38, 0.82, 0.36), PAL["brick_header_dark"], bevel_amt=0.010)
    mb.add_box((0.52, 0.0, 0.84), (0.42, 0.86, 0.055), PAL["steel_hbeam_dark"], bevel_amt=0.006)
    # Ustunde hadde merdaneleri / etriye bukme kafasi, H-profil celik kiris, rebar demeti ve tork anahtari
    mb.add_box((0.52, 0.42, 1.06), (0.24, 0.22, 0.17), PAL["shoring_prop_blue"], bevel_amt=0.006)
    mb.add_box((0.52, 0.42, 1.38), (0.06, 0.06, 0.18), PAL["brass_collar"], bevel_amt=0.005)
    mb.add_box((0.36, -0.32, 0.98), (0.12, 0.44, 0.085), PAL["steel_hbeam_bluegrey"], bevel_amt=0.005)
    for rx in (0.64, 0.72, 0.80):
        mb.add_box((rx, -0.22, 0.94), (0.018, 0.56, 0.018), PAL["rebar_ribbed_steel"], bevel_amt=0.003)
    mb.add_box(
        (0.54, -0.58, 1.02),
        (0.24, 0.045, 0.032),
        PAL["strike_wedge_ochre"],
        rot_euler=(0.0, 0.0, 0.35),
        bevel_amt=0.004,
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

    expanded_9schools_specs = [
        ("mod_grooved_log_post_3m", build_mod_grooved_log_post_3m),
        ("mod_temp_wood_brace_2m", build_mod_temp_wood_brace_2m),
        ("mod_wooden_arch_centering_2m", build_mod_wooden_arch_centering_2m),
        ("mod_sliding_earth_formwork_2m", build_mod_sliding_earth_formwork_2m),
        ("mod_cob_curved_wall_2m", build_mod_cob_curved_wall_2m),
        ("mod_rammed_earth_wall_2x2m", build_mod_rammed_earth_wall_2x2m),
        ("mod_english_bond_brick_2x2m", build_mod_english_bond_brick_2x2m),
        ("mod_timber_ring_beam_2m", build_mod_timber_ring_beam_2m),
        ("mod_platform_stud_frame_2x2m", build_mod_platform_stud_frame_2x2m),
        ("mod_osb_sheathing_panel_2x2m", build_mod_osb_sheathing_panel_2x2m),
        ("mod_rebar_foundation_cage_2m", build_mod_rebar_foundation_cage_2m),
        ("mod_concrete_column_formwork_3m", build_mod_concrete_column_formwork_3m),
        ("mod_telescopic_shoring_2x3m", build_mod_telescopic_shoring_2x3m),
        ("mod_steel_h_column_3m", build_mod_steel_h_column_3m),
        ("mod_steel_x_brace_2x2m", build_mod_steel_x_brace_2x2m),
        ("mod_metal_deck_slab_2x2m", build_mod_metal_deck_slab_2x2m),
        ("mod_station_sawhorse_kiln_bundle", build_mod_station_sawhorse_kiln_bundle),
        ("mod_station_trough_chisel_steel", build_mod_station_trough_chisel_steel),
    ]

    for asset_id, builder_fn in expanded_9schools_specs:
        obj = builder_fn(mat)
        out_glb = os.path.join(OUT_MODULAR_DIR, f"{asset_id}.glb")
        export_single_piece_glb(obj, out_glb)
        tris = len(obj.data.polygons)
        size_kb = os.path.getsize(out_glb) / 1024.0
        print(f"[Expanded9SchoolsKitBuilder] Exported {asset_id}.glb ({tris} tris, {size_kb:.1f} KB)")


if __name__ == "__main__":
    main()
