"""
Stratocraft Otonom 3B Fabrika — Ekol 4 Anitsal Sutun, Kemer, Tonoz, Kubbe ve Payanda Ureticisi (Blender 4.2).

Kullanim (0 gomulu doku, UV-free COLOR_0, < 16.0 MB VRAM):
    blender --background --python Tools/AutonomousAssetFactory/generators/monumental_arch_dome_builder.py

Uretilen 10 Anitsal Ekol 4 .glb Parca (Assets/Art/Generated/Structures_Modular/*.glb):
    1. mod_classical_column_base.glb     — Attika-Iyon / Osmanli mermer sutun kaidesi (0.86x0.86x0.52m)
    2. mod_classical_column_shaft_3m.glb — 16 yivli, entasisli mermer sutun govdesi (3.0m, Ø 0.56->0.48m)
    3. mod_classical_column_capital.glb  — Mukarnas / Korint mermer sutun basligi + abakus (0.92x0.92x0.64m)
    4. mod_arch_voussoir_segment.glb     — 45° yay dilimli 3 kama tasli kemer segmenti (Voussoir + kenet)
    5. mod_arch_keystone.glb             — Kabartma rozetli anitsal kemer kilit tasi (0.54x0.60x0.62m)
    6. mod_barrel_vault_2x2m.glb         — 2.0x2.0m aciklikli tugla/kesme tas kaburgali besik tonoz
    7. mod_dome_rib_segment.glb          — 4.0m capli kubbenin 45° (1/8) kasetli kaburga ve kursun dilimi
    8. mod_pendentive_corner.glb         — Kare plandan kubbe kasnagina gecis saglayan kuresel ucgen pandantif
    9. mod_exterior_buttress_3m.glb      — Yanal itmeyi dengeleyen 3.0m kademeli kesme tas dis payanda
   10. mod_iron_tie_rod_2m.glb           — Kemer ayaklarini baglayan 2.0m dovme demir gergi cubugu + rozet
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

# Ekol 4 Anitsal Mermer, Kesme Kirectasi, Horasan Tuglasi, Kursun ve Dovme Demir Paleti (sRGB -> COLOR_0)
# R kanali asla 0, 3 veya 11 secilmez.
PAL = {
    "marble_ivory": (228, 224, 214, 250),
    "marble_warm": (214, 206, 194, 248),
    "marble_vein": (184, 178, 168, 242),
    "marble_shade": (162, 156, 146, 236),
    "ashlar_gold": (196, 184, 162, 246),
    "ashlar_mid": (176, 166, 148, 244),
    "ashlar_dark": (134, 128, 118, 234),
    "lime_mortar": (206, 200, 188, 232),
    "horasan_brick": (182, 96, 72, 244),
    "horasan_brick_dark": (152, 76, 56, 238),
    "lead_dome": (104, 110, 118, 242),
    "lead_cramp": (124, 130, 138, 244),
    "wrought_iron": (64, 68, 76, 240),
    "wrought_iron_dark": (48, 52, 58, 234),
    "bronze_collar": (168, 134, 78, 246),
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
    mat = bpy.data.materials.new(name="M_MonumentalVertexColorPreview")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    vcol = nt.nodes.new("ShaderNodeVertexColor")
    vcol.layer_name = "Col"
    bsdf.inputs["Roughness"].default_value = 0.62
    bsdf.inputs["Specular IOR Level"].default_value = 0.28
    nt.links.new(vcol.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


class MonumentalMeshBuilder:
    """UV-free COLOR_0 Ekol 4 anitsal mimari bmesh insacisi."""

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
                shade *= 0.85
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

    def add_lathe_profile(self, z_radius_col_list, segs=16, center_xy=(0.0, 0.0)):
        """(z, radius, rgba_u8) istasyon listesini dairesel donel yuzey olarak orer."""
        rings = []
        cx, cy = center_xy
        for z_val, r_val, _ in z_radius_col_list:
            ring = []
            for s in range(segs):
                ang = (2.0 * math.pi * s) / segs
                pt = Vector((cx + math.cos(ang) * r_val, cy + math.sin(ang) * r_val, z_val))
                ring.append(self.bm.verts.new(pt))
            rings.append(ring)

        for i in range(len(rings) - 1):
            r0 = rings[i]
            r1 = rings[i + 1]
            col = z_radius_col_list[i][2]
            band_faces = []
            for s in range(segs):
                sn = (s + 1) % segs
                try:
                    f = self.bm.faces.new((r0[s], r0[sn], r1[sn], r1[s]))
                    band_faces.append(f)
                except ValueError:
                    pass
            bmesh.ops.recalc_face_normals(self.bm, faces=band_faces)
            self.paint_faces(band_faces, col)

        # Alt ve ust kapaklar
        for end_idx, flip in ((0, True), (-1, False)):
            ring = rings[end_idx]
            z_end = z_radius_col_list[end_idx][0]
            col_end = z_radius_col_list[end_idx][2]
            c_vert = self.bm.verts.new(Vector((cx, cy, z_end)))
            cap_faces = []
            for s in range(segs):
                sn = (s + 1) % segs
                try:
                    if flip:
                        f = self.bm.faces.new((ring[sn], ring[s], c_vert))
                    else:
                        f = self.bm.faces.new((ring[s], ring[sn], c_vert))
                    cap_faces.append(f)
                except ValueError:
                    pass
            bmesh.ops.recalc_face_normals(self.bm, faces=cap_faces)
            self.paint_faces(cap_faces, col_end)

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
# 10 EKOL 4 ANITSAL PARCA URETICILERI
# ==============================================================================

def build_mod_classical_column_base(mat):
    """Attika-Iyon / Osmanli mermer sutun kaidesi (0.86m x 0.86m kare plint + torus/skotia, H=0.52m)."""
    mb = MonumentalMeshBuilder("Mod_Classical_Column_Base")
    # Alt kare mermer plint (Z in [0.0..0.18])
    mb.add_box((0.0, 0.0, 0.09), (0.43, 0.43, 0.09), PAL["marble_warm"], bevel_amt=0.014)
    mb.add_box((0.0, 0.0, 0.195), (0.38, 0.38, 0.025), PAL["marble_vein"], bevel_amt=0.008)
    # Alt torus -> icbukey skotia -> ust torus -> sutun oturtma bilezigi
    profile = [
        (0.21, 0.36, PAL["marble_ivory"]),
        (0.26, 0.38, PAL["marble_ivory"]),
        (0.31, 0.34, PAL["marble_vein"]),
        (0.35, 0.30, PAL["marble_shade"]),
        (0.40, 0.34, PAL["marble_warm"]),
        (0.45, 0.33, PAL["marble_ivory"]),
        (0.50, 0.285, PAL["marble_ivory"]),
    ]
    mb.add_lathe_profile(profile, segs=16)
    # Ust merkez kursunlu bronz zivana pimi
    mb.add_box((0.0, 0.0, 0.51), (0.045, 0.045, 0.02), PAL["lead_cramp"], bevel_amt=0.004)
    return mb.finalize_object(mat)


def build_mod_classical_column_shaft_3m(mat):
    """3.0m yukseklikte, entasis daralmali (Ø 0.56m -> 0.48m), 16 yivli mermer sutun govdesi."""
    mb = MonumentalMeshBuilder("Mod_Classical_Column_Shaft_3m")
    # Alt ve ust bronz/mermer astragal bilezikleri
    mb.add_lathe_profile(
        [
            (0.00, 0.295, PAL["bronze_collar"]),
            (0.07, 0.285, PAL["bronze_collar"]),
            (0.11, 0.280, PAL["marble_warm"]),
        ],
        segs=16,
    )
    mb.add_lathe_profile(
        [
            (2.89, 0.242, PAL["marble_warm"]),
            (2.94, 0.268, PAL["bronze_collar"]),
            (3.00, 0.262, PAL["bronze_collar"]),
        ],
        segs=16,
    )

    # 16 yivli (fluted) = 32 kose noktali entasis mermer govde (Z in [0.11..2.89])
    flutes = 16
    verts_per_ring = flutes * 2
    drums = 7
    z_stations = [0.11 + (2.78 * i) / drums for i in range(drums + 1)]
    rings = []
    tones = (PAL["marble_ivory"], PAL["marble_warm"], PAL["marble_ivory"], PAL["marble_vein"])

    for z_val in z_stations:
        t = (z_val - 0.11) / 2.78
        # Klasik entasis egimi: ortada hafif dolgun, yukariya dogru zarif daralma
        r_outer = 0.280 - 0.038 * (t ** 1.25)
        r_inner = r_outer - 0.016
        ring = []
        for k in range(verts_per_ring):
            ang = (2.0 * math.pi * k) / verts_per_ring
            r_use = r_outer if (k % 2 == 0) else r_inner
            ring.append(mb.bm.verts.new(Vector((math.cos(ang) * r_use, math.sin(ang) * r_use, z_val))))
        rings.append(ring)

    for i in range(len(rings) - 1):
        r0 = rings[i]
        r1 = rings[i + 1]
        col = tones[i % len(tones)]
        band_faces = []
        for k in range(verts_per_ring):
            kn = (k + 1) % verts_per_ring
            try:
                f = mb.bm.faces.new((r0[k], r0[kn], r1[kn], r1[k]))
                band_faces.append(f)
            except ValueError:
                pass
        bmesh.ops.recalc_face_normals(mb.bm, faces=band_faces)
        mb.paint_faces(band_faces, col, z_ao_origin=0.0, z_ao_span=3.0, z_ao_strength=0.10)

    return mb.finalize_object(mat)


def build_mod_classical_column_capital(mat):
    """0.92m x 0.92m x 0.64m mukarnas ve volüt oymali mermer sutun basligi + genis abakus tablasi."""
    mb = MonumentalMeshBuilder("Mod_Classical_Column_Capital")
    # Alt boyun bilezigi (astragal) + canak govde (echinus, Z in [0.0..0.38])
    mb.add_lathe_profile(
        [
            (0.00, 0.255, PAL["bronze_collar"]),
            (0.06, 0.268, PAL["marble_warm"]),
            (0.16, 0.310, PAL["marble_ivory"]),
            (0.28, 0.365, PAL["marble_ivory"]),
            (0.38, 0.405, PAL["marble_warm"]),
        ],
        segs=16,
    )
    # 4 kosede disari acilan mukarnas / akantus volut dilimleri
    for sx in (-1.0, 1.0):
        for sy in (-1.0, 1.0):
            mb.add_box(
                (sx * 0.25, sy * 0.25, 0.26),
                (0.12, 0.12, 0.11),
                PAL["marble_warm"],
                rot_euler=(sy * 0.22, -sx * 0.22, 0.0),
                bevel_amt=0.012,
            )
            mb.add_box(
                (sx * 0.33, sy * 0.33, 0.41),
                (0.11, 0.11, 0.085),
                PAL["marble_ivory"],
                bevel_amt=0.012,
            )
    # Ust tasiyici kare Abakus (Abacus) ve kemer uzengi yastigi (Impost, Z in [0.46..0.64])
    mb.add_box((0.0, 0.0, 0.51), (0.44, 0.44, 0.055), PAL["marble_ivory"], bevel_amt=0.014)
    mb.add_box((0.0, 0.0, 0.60), (0.46, 0.46, 0.040), PAL["marble_warm"], bevel_amt=0.012)
    return mb.finalize_object(mat)


def build_mod_arch_voussoir_segment(mat):
    """
    Ekol 4: 45° yay dilimini (0°..45°) orten 3 kama tasli (Voussoir) harcli kemer segmenti;
    R_in=1.00m, R_out=1.42m, derinlik=0.52m, ustte kursunlu demir kenet (LeadCramp) yuvasi.
    """
    mb = MonumentalMeshBuilder("Mod_Arch_Voussoir_Segment")
    # Pivot tabanda Z=0.0m olacak sekilde 3 radyal kama tasi
    stones = 3
    ang_start = 0.08
    ang_end = 0.80
    d_ang = (ang_end - ang_start) / stones
    r_mid = 1.18
    base_z_shift = -r_mid * math.cos(ang_end) + 0.22
    tones = (PAL["ashlar_gold"], PAL["marble_warm"], PAL["ashlar_mid"])

    for i in range(stones):
        ang = ang_start + (i + 0.5) * d_ang
        px = r_mid * math.sin(ang)
        pz = r_mid * math.cos(ang) + base_z_shift
        # Kama tasi + harc derzi
        mb.add_box(
            (px, 0.0, pz),
            (0.155, 0.26, 0.21),
            tones[i % len(tones)],
            rot_euler=(0.0, ang, 0.0),
            bevel_amt=0.014,
        )
        # Ic kemer karin (intrados) profil silmesi
        px_in = (r_mid - 0.19) * math.sin(ang)
        pz_in = (r_mid - 0.19) * math.cos(ang) + base_z_shift
        mb.add_box(
            (px_in, 0.0, pz_in),
            (0.145, 0.272, 0.032),
            PAL["marble_ivory"],
            rot_euler=(0.0, ang, 0.0),
            bevel_amt=0.008,
        )
        # Kursunlu demir kenet (LeadCramp)
        if i < stones - 1:
            ang_j = ang_start + (i + 1.0) * d_ang
            jx = (r_mid + 0.18) * math.sin(ang_j)
            jz = (r_mid + 0.18) * math.cos(ang_j) + base_z_shift
            mb.add_box(
                (jx, 0.0, jz),
                (0.065, 0.032, 0.018),
                PAL["lead_cramp"],
                rot_euler=(0.0, ang_j, 0.0),
                bevel_amt=0.004,
            )
    return mb.finalize_object(mat)


def build_mod_arch_keystone(mat):
    """
    Ekol 4: Kemer tepe noktasini kilitleyen kama bicimli, disa taskin ve kabartma rozetli
    anitsal Kemer Kilit Tasi (Keystone, 0.56m x 0.60m x 0.64m, Z in [0.0..0.64]).
    """
    mb = MonumentalMeshBuilder("Mod_Arch_Keystone")
    # Alt daralan kama koku + ust genisleyen kilit govdesi
    mb.add_box((0.0, 0.0, 0.16), (0.20, 0.28, 0.16), PAL["marble_warm"], bevel_amt=0.014)
    mb.add_box((0.0, 0.0, 0.42), (0.26, 0.29, 0.18), PAL["marble_ivory"], bevel_amt=0.016)
    # Yan egimli kama kanatlari (voussoir temas yuzeyleri)
    for sign in (-1.0, 1.0):
        mb.add_box(
            (sign * 0.21, 0.0, 0.32),
            (0.065, 0.275, 0.25),
            PAL["ashlar_gold"],
            rot_euler=(0.0, -sign * 0.24, 0.0),
            bevel_amt=0.012,
        )
    # On ve arka cephede kabartma kilit armasi + ust abakus taci
    for sy in (-0.295, 0.295):
        mb.add_box((0.0, sy, 0.34), (0.12, 0.022, 0.15), PAL["bronze_collar"], bevel_amt=0.006)
    mb.add_box((0.0, 0.0, 0.61), (0.28, 0.31, 0.035), PAL["marble_ivory"], bevel_amt=0.010)
    # Ust iki yanda demir kenet yuvalari
    for sign in (-1.0, 1.0):
        mb.add_box((sign * 0.20, 0.0, 0.635), (0.06, 0.028, 0.012), PAL["lead_cramp"], bevel_amt=0.003)
    return mb.finalize_object(mat)


def build_mod_barrel_vault_2x2m(mat):
    """
    Ekol 4: 2.0m x 2.0m acikligi orten yarim silindir tas ve Horasan tuglasi Besik Tonoz
    (Barrel Vault); on/arka tas kemer kaburgalari ve ust duz harcli doseme sirti.
    """
    mb = MonumentalMeshBuilder("Mod_Barrel_Vault_2x2m")
    # Yan uzengi duvar yastiklari (springing blocks, X = ±0.92m, Z in [0.0..0.32])
    for sign in (-1.0, 1.0):
        mb.add_box((sign * 0.91, 0.0, 0.16), (0.11, 1.00, 0.16), PAL["ashlar_mid"], bevel_amt=0.012)

    # Yarim daire tonoz kabugu: 9 radyal dilim (-1.28 .. +1.28 rad), Y boyunca 2.0m uzanir
    arch_segs = 9
    r_arch = 0.88
    z_base = 0.24
    for k in range(arch_segs):
        t = -1.0 + (2.0 * (k + 0.5)) / arch_segs
        ang = t * 1.32
        px = r_arch * math.sin(ang)
        pz = z_base + r_arch * math.cos(ang)
        col = PAL["horasan_brick"] if (k % 2 == 0) else PAL["horasan_brick_dark"]
        if k == arch_segs // 2:
            col = PAL["marble_ivory"]
        # Tonoz tugla kemer kabugu
        mb.add_box(
            (px, 0.0, pz),
            (0.165, 0.98, 0.11),
            col,
            rot_euler=(0.0, ang, 0.0),
            bevel_amt=0.010,
        )
        # On (Y=-0.92) ve Arka (Y=+0.92) kesme tas tasiyici tonoz kemer kaburgalari (transverse ribs)
        for ry in (-0.91, 0.91):
            mb.add_box(
                (px * 0.97, ry, pz - 0.02),
                (0.155, 0.095, 0.135),
                PAL["ashlar_gold"],
                rot_euler=(0.0, ang, 0.0),
                bevel_amt=0.010,
            )

    # Tonoz sirti ustu duz harcli tas doseme dolgusu (extrados walking deck, Z in [1.12..1.26])
    mb.add_box((0.0, 0.0, 1.20), (0.98, 0.99, 0.06), PAL["lime_mortar"], bevel_amt=0.012)
    return mb.finalize_object(mat)


def build_mod_dome_rib_segment(mat):
    """
    Ekol 4: 4.0m capli (R=2.0m) anitsal kubbenin 45° (1/8) kuresel kabuk + tasiyici tas kaburga
    ve dis kursun/terracotta kaplama dilimi (Z in [0.0..2.0m]).
    """
    mb = MonumentalMeshBuilder("Mod_Dome_Rib_Segment")
    # Kubbe kasnak oturtma kaidesi (drum base arc, Z in [0.0..0.24])
    wedge_half_ang = math.pi / 8.0  # 22.5° (toplam 45° dilim)
    r_base = 1.85
    steps_phi = 6  # Dikey elevasyon halkalari (0° -> 82°)

    for p in range(steps_phi):
        phi0 = (p / steps_phi) * (math.pi * 0.44)
        phi1 = ((p + 1) / steps_phi) * (math.pi * 0.44)
        phi_mid = 0.5 * (phi0 + phi1)

        r_horiz = r_base * math.cos(phi_mid)
        z_height = r_base * math.sin(phi_mid)
        chord_w = max(0.14, r_horiz * math.sin(wedge_half_ang))
        arc_len = (r_base * (phi1 - phi0)) * 0.54

        # 1) Dis kursun kubbe kaplama kabugu (Lead Dome Shell)
        mb.add_box(
            (0.0, r_horiz, z_height + 0.12),
            (chord_w, 0.11, arc_len),
            PAL["lead_dome"] if (p % 2 == 0) else PAL["wrought_iron"],
            rot_euler=(phi_mid, 0.0, 0.0),
            bevel_amt=0.010,
        )
        # 2) Ic Horasan tuglasi kasetli kubbe karini (Intrados Coffers)
        mb.add_box(
            (0.0, r_horiz - 0.09, z_height + 0.09),
            (chord_w * 0.84, 0.065, arc_len * 0.86),
            PAL["horasan_brick"] if (p % 2 == 0) else PAL["ashlar_gold"],
            rot_euler=(phi_mid, 0.0, 0.0),
            bevel_amt=0.008,
        )
        # 3) Yan tasiyici mermer kubbe kaburgasi (Meridian Rib)
        mb.add_box(
            (0.0, r_horiz - 0.13, z_height + 0.07),
            (0.095, 0.085, arc_len * 0.98),
            PAL["marble_ivory"],
            rot_euler=(phi_mid, 0.0, 0.0),
            bevel_amt=0.008,
        )

    # Tepe oculus / alem kilit bilezigi
    mb.add_box((0.0, 0.22, 1.94), (0.18, 0.18, 0.08), PAL["bronze_collar"], bevel_amt=0.010)
    return mb.finalize_object(mat)


def build_mod_pendentive_corner(mat):
    """
    Ekol 4: Kare kemer/duvar kosesinden (2.0m x 2.0m) dairesel kubbe kasnagina gecisi saglayan
    kuresel ucgen Pandantif Kose Gecisi (Pendentive Corner, Z in [0.0..1.80m]).
    """
    mb = MonumentalMeshBuilder("Mod_Pendentive_Corner")
    # Alt kose uzengi tasi (console corbel, Z in [0.0..0.32])
    mb.add_box((0.0, 0.0, 0.16), (0.26, 0.26, 0.16), PAL["marble_warm"], bevel_amt=0.014)

    # Yukari ciktikca genisleyen ve one kavislenen 5 kademeli kuresel ucgen / mukarnas pandantif
    tiers = 5
    for t in range(tiers):
        frac = (t + 1) / tiers
        z_c = 0.28 + t * 0.28
        span_xy = 0.28 + frac * 0.56
        overhang = frac * 0.34
        col = PAL["ashlar_gold"] if (t % 2 == 0) else PAL["marble_ivory"]
        # Merkez diyagonal yelpaze dilimi
        mb.add_box(
            (overhang * 0.5, overhang * 0.5, z_c),
            (span_xy, 0.22, 0.145),
            col,
            rot_euler=(-0.22 * frac, 0.0, -0.785398),
            bevel_amt=0.012,
        )
        # Sol ve sag kemer yanak kaburgalari
        mb.add_box(
            (span_xy * 0.65, 0.0, z_c),
            (0.24, 0.18, 0.14),
            PAL["marble_warm"],
            rot_euler=(0.0, -0.18 * frac, 0.0),
            bevel_amt=0.010,
        )
        mb.add_box(
            (0.0, span_xy * 0.65, z_c),
            (0.18, 0.24, 0.14),
            PAL["marble_warm"],
            rot_euler=(0.18 * frac, 0.0, 0.0),
            bevel_amt=0.010,
        )

    # Ust dairesel kubbe kasnak kornisi (Circular Drum Ring Segment, Z in [1.62..1.80])
    mb.add_box(
        (0.32, 0.32, 1.71),
        (0.86, 0.24, 0.09),
        PAL["marble_ivory"],
        rot_euler=(0.0, 0.0, -0.785398),
        bevel_amt=0.014,
    )
    return mb.finalize_object(mat)


def build_mod_exterior_buttress_3m(mat):
    """
    Ekol 4: Kemer ve kubbe yanal itmesini (Lateral Thrust) karsilayan 3.0m yukseklikte,
    3 kademeli ve yagmur damlalikli (weathering slope) agir kesme tas Dis Payanda (Buttress).
    """
    mb = MonumentalMeshBuilder("Mod_Exterior_Buttress_3m")
    # 1. Alt genis temel kaidesi (Z in [0.0..0.44], derinlik Y=1.24m, genislik X=0.82m)
    mb.add_box((0.0, 0.0, 0.22), (0.41, 0.62, 0.22), PAL["ashlar_dark"], bevel_amt=0.016)
    # 2. Alt gövde kademesi (Z in [0.44..1.40], derinlik Y=1.06m) + eğimli damlalık şapkası
    mb.add_box((0.0, 0.08, 0.92), (0.37, 0.53, 0.48), PAL["ashlar_mid"], bevel_amt=0.014)
    mb.add_box(
        (0.0, -0.24, 1.44),
        (0.39, 0.24, 0.065),
        PAL["marble_warm"],
        rot_euler=(0.42, 0.0, 0.0),
        bevel_amt=0.010,
    )
    # 3. Orta gövde kademesi (Z in [1.40..2.32], derinlik Y=0.82m) + eğimli damlalık
    mb.add_box((0.0, 0.20, 1.86), (0.34, 0.41, 0.46), PAL["ashlar_gold"], bevel_amt=0.014)
    mb.add_box(
        (0.0, -0.04, 2.36),
        (0.36, 0.20, 0.06),
        PAL["marble_warm"],
        rot_euler=(0.45, 0.0, 0.0),
        bevel_amt=0.010,
    )
    # 4. Ust kemer dayama boynu ve egimli zirve taci (Z in [2.32..3.00])
    mb.add_box((0.0, 0.30, 2.64), (0.31, 0.31, 0.32), PAL["ashlar_mid"], bevel_amt=0.012)
    mb.add_box(
        (0.0, 0.28, 2.94),
        (0.34, 0.35, 0.065),
        PAL["marble_ivory"],
        rot_euler=(0.38, 0.0, 0.0),
        bevel_amt=0.010,
    )
    return mb.finalize_object(mat)


def build_mod_iron_tie_rod_2m(mat):
    """
    Ekol 4: Osmanli/Klasik kemer ayaklarini birbirine baglayarak yanal itmeyi (Lateral Thrust)
    sifirlayan 2.16m dovme demir gergi cubugu (Iron Tie-Rod) + gerdirme bilezigi ve ankraj rozetleri.
    """
    mb = MonumentalMeshBuilder("Mod_Iron_Tie_Rod_2m")
    # Ana kare dovme demir gergi mili (X in [-1.06..+1.06], Z merkez = 0.14m, taban Z=0.0m)
    mb.add_box((0.0, 0.0, 0.14), (1.06, 0.028, 0.028), PAL["wrought_iron"], bevel_amt=0.005)
    # Merkez gerdirme manşonu ve kama kilit bilezigi (Turnbuckle Coupler)
    mb.add_box((0.0, 0.0, 0.14), (0.16, 0.046, 0.046), PAL["wrought_iron_dark"], bevel_amt=0.006)
    mb.add_box((0.0, 0.0, 0.14), (0.032, 0.055, 0.072), PAL["bronze_collar"], bevel_amt=0.004)

    # Her iki ucta kemer/sutun basligina gomulen kursun yastiklar + S-kama ankraj plakalari
    for sign in (-1.0, 1.0):
        # Kursun dokum yatak yastigi (Lead Bedding Pad, tabani Z=0.0m)
        mb.add_box((sign * 0.96, 0.0, 0.07), (0.11, 0.11, 0.07), PAL["lead_cramp"], bevel_amt=0.008)
        # Dis duvar ankraj rozeti ve dikey S-kama kamasi (Wall Anchor Plate & Cotter Pin)
        mb.add_box((sign * 1.04, 0.0, 0.14), (0.024, 0.14, 0.14), PAL["wrought_iron_dark"], bevel_amt=0.006)
        mb.add_box(
            (sign * 1.075, 0.0, 0.14),
            (0.018, 0.035, 0.135),
            PAL["wrought_iron"],
            rot_euler=(0.25 * sign, 0.0, 0.0),
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

    monumental_specs = [
        ("mod_classical_column_base", build_mod_classical_column_base),
        ("mod_classical_column_shaft_3m", build_mod_classical_column_shaft_3m),
        ("mod_classical_column_capital", build_mod_classical_column_capital),
        ("mod_arch_voussoir_segment", build_mod_arch_voussoir_segment),
        ("mod_arch_keystone", build_mod_arch_keystone),
        ("mod_barrel_vault_2x2m", build_mod_barrel_vault_2x2m),
        ("mod_dome_rib_segment", build_mod_dome_rib_segment),
        ("mod_pendentive_corner", build_mod_pendentive_corner),
        ("mod_exterior_buttress_3m", build_mod_exterior_buttress_3m),
        ("mod_iron_tie_rod_2m", build_mod_iron_tie_rod_2m),
    ]

    for asset_id, builder_fn in monumental_specs:
        obj = builder_fn(mat)
        out_glb = os.path.join(OUT_MODULAR_DIR, f"{asset_id}.glb")
        export_single_piece_glb(obj, out_glb)
        tris = len(obj.data.polygons)
        size_kb = os.path.getsize(out_glb) / 1024.0
        print(f"[MonumentalArchDomeBuilder] Exported {asset_id}.glb ({tris} tris, {size_kb:.1f} KB)")


if __name__ == "__main__":
    main()
