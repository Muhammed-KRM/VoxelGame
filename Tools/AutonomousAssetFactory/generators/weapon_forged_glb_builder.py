"""
Stratocraft Otonom 3B Fabrika — Gercek Tarihsel Dovme Kilic Ureticisi (Blender 4.2).

Kullanim (PATH uzerindeki blender ile, 0 gomulu doku, UV-free COLOR_0):
    blender --background --python Tools/AutonomousAssetFactory/generators/weapon_forged_glb_builder.py

Ciktilar (Assets/Art/Generated/Weapons/*.glb):
    1. weapon_longsword.glb   — 1.85 kg Avrupa Uzun Kilici (elmas/6-gen namlu, derin fuller, pirinc balcak, deri kabza, topuz)
    2. weapon_katana.glb      — 1.35 kg Kavisli Katana (sori egrisi, shinogi sirti, parlak hamon agzi, pirinc habaki, dairesel tsuba, tsuka-ito sargisi, fuchi/kashira)
    3. weapon_zweihander.glb  — 5.60 kg Alman Zweihander (ince 6-gen namlu + fuller, Parierhaken yan kancalari, deri ricasso, genis balcak + yan koruma halkalari, cift el kabza, fasetli topuz)
    4. weapon_rapier.glb      — 0.95 kg Ronesans Eskrim Kilici (suslu canak + kafes el siperi, ince quillonlar, tel sargili kabza, igne kesitli delici namlu)

KRITIK KOORDINAT STANDARDI (Unity +Y = Blender +Z):
    - Balcak / Tsuba alt yuzu (crossguard / tsuba underside) : y = 0.000m
    - Kabza & Topuz (hilt / grip / pommel)                   : y < 0.000m
    - Keskin Namlu (blade)                                   : y > +0.020m
"""

import math
import os

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, "..", "..", ".."))
OUT_WEAPONS_DIR = os.path.join(REPO_ROOT, "Assets", "Art", "Generated", "Weapons")


# Dogrudan RGB Vertex Color paleti (Stratocraft/VertexColorUberLit _UsePaletteLUT = 0)
# Not: R kanali 0, 3 veya 11 secilmez (shader maskeleriyle %100 ayrik).
PAL = {
    # Celik namlu tonlari
    "steel_edge": (228, 236, 246, 255),
    "steel_bright": (204, 214, 228, 255),
    "steel_mid": (166, 176, 192, 255),
    "steel_fuller": (92, 102, 118, 255),
    "steel_spine": (78, 88, 104, 255),
    "steel_tip": (240, 246, 254, 255),
    # Balcak / demir aksam
    "guard_steel": (106, 114, 128, 255),
    "guard_steel_hi": (146, 156, 172, 255),
    "guard_dark": (68, 74, 86, 255),
    # Antik pirinc / altin tsuba & bilezikler
    "brass_main": (196, 158, 66, 255),
    "brass_bright": (226, 188, 88, 255),
    "brass_dark": (142, 110, 44, 255),
    # Deri kabza & ricasso
    "leather_dark": (66, 38, 22, 255),
    "leather_mid": (92, 54, 32, 255),
    "leather_hi": (114, 68, 40, 255),
    # Katana tsuka-ito, samegawa & hamon
    "tsuka_wrap": (38, 34, 46, 255),
    "tsuka_accent": (148, 36, 42, 255),
    "samegawa": (212, 204, 188, 255),
    "katana_hamon": (236, 242, 252, 255),
    "katana_mune": (82, 92, 108, 255),
    # Rapier gumus canak & bordo kabza
    "rapier_grip": (104, 30, 38, 255),
    "rapier_wire": (214, 178, 82, 255),
    "rapier_cup": (192, 202, 216, 255),
    "rapier_cup_inner": (122, 130, 144, 255),
}


def srgb_to_linear_rgba(rgba_u8):
    """Blender 4.2 vertex color -> glTF COLOR_0 linear donusumu (R != 0, 3, 11)."""

    def _c(v):
        x = max(0.0, min(1.0, v / 255.0))
        return x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4

    r_safe = int(rgba_u8[0])
    if r_safe in (0, 3, 11):
        r_safe += 1
    return (_c(r_safe), _c(rgba_u8[1]), _c(rgba_u8[2]), rgba_u8[3] / 255.0)


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def create_weapon_preview_material():
    mat = bpy.data.materials.new(name="M_WeaponVertexColor")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    vcol = nt.nodes.new("ShaderNodeVertexColor")
    vcol.layer_name = "Col"
    bsdf.inputs["Roughness"].default_value = 0.32
    bsdf.inputs["Metallic"].default_value = 0.65
    nt.links.new(vcol.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


class SwordBuilder:
    """
    Tek bir dovme kilicin yuksek detayli bmesh ureticisi.
    Blender koordinatlari:
      +Z = namlu uzunluk ekseni (Unity +Y)
      +X = namlu genisligi / balcak ekseni (Unity -X)
      +Y = namlu kalinligi (Unity -Z)
    """

    def __init__(self, name: str):
        self.name = name
        self.bm = bmesh.new()
        self.color_layer = self.bm.loops.layers.color.new("Col")

    def _paint_faces(self, faces, rgba_u8):
        lin = srgb_to_linear_rgba(rgba_u8)
        for f in faces:
            for loop in f.loops:
                loop[self.color_layer] = lin

    def add_profile_loft(
        self,
        sections,
        cap_bottom=True,
        cap_top=True,
    ):
        """
        Kesit dizisinden cok yuzeyli loft uretir.
        sections: list of (pts_3d, face_colors_per_edge)
          - pts_3d: [(x, y, z), ...] (hepsi ayni nokta sayisinda, saat yonunun tersi sirali)
          - face_colors_per_edge: [rgba_u8, ...] (kenar basina yuz rengi)
        """
        ring_verts = []
        for pts, _ in sections:
            rv = [self.bm.verts.new(Vector(p)) for p in pts]
            ring_verts.append(rv)

        n = len(ring_verts[0])
        all_faces = []

        for r in range(len(ring_verts) - 1):
            r0 = ring_verts[r]
            r1 = ring_verts[r + 1]
            edge_cols = sections[r][1]
            for i in range(n):
                jn = (i + 1) % n
                try:
                    f = self.bm.faces.new((r0[i], r0[jn], r1[jn], r1[i]))
                    all_faces.append(f)
                    col = edge_cols[i % len(edge_cols)]
                    self._paint_faces([f], col)
                except ValueError:
                    pass

        if cap_bottom and len(ring_verts) > 0:
            try:
                f = self.bm.faces.new(list(reversed(ring_verts[0])))
                all_faces.append(f)
                self._paint_faces([f], sections[0][1][0])
            except ValueError:
                pass

        if cap_top and len(ring_verts) > 0:
            try:
                f = self.bm.faces.new(ring_verts[-1])
                all_faces.append(f)
                self._paint_faces([f], sections[-1][1][0])
            except ValueError:
                pass

        bmesh.ops.recalc_face_normals(self.bm, faces=all_faces)
        return all_faces

    def add_point_tip(self, base_pts, tip_pt, edge_cols):
        """Son namlu kesitini keskin bir uc noktasina (tip_pt) kapatir."""
        rv = [self.bm.verts.new(Vector(p)) for p in base_pts]
        tv = self.bm.verts.new(Vector(tip_pt))
        n = len(rv)
        faces = []
        for i in range(n):
            jn = (i + 1) % n
            try:
                f = self.bm.faces.new((rv[i], rv[jn], tv))
                faces.append(f)
                self._paint_faces([f], edge_cols[i % len(edge_cols)])
            except ValueError:
                pass
        bmesh.ops.recalc_face_normals(self.bm, faces=faces)
        return faces

    def add_lathe_rings(self, rings, segs=16, rgba_u8=None, cap_bottom=True, cap_top=True, offset=(0, 0, 0)):
        """
        Eksenel torna (pommel, kabza, bilezik, tsuba):
        rings: [(z, rx, ry, cx, cy, col_override_or_None), ...]
        """
        ox, oy, oz = offset
        sections = []
        for entry in rings:
            z, rx, ry = entry[0], entry[1], entry[2]
            cx = entry[3] if len(entry) > 3 else 0.0
            cy = entry[4] if len(entry) > 4 else 0.0
            col = entry[5] if len(entry) > 5 and entry[5] is not None else rgba_u8
            pts = []
            for s in range(segs):
                a = (2.0 * math.pi * s) / segs
                pts.append((ox + cx + math.cos(a) * rx, oy + cy + math.sin(a) * ry, oz + z))
            sections.append((pts, [col] * segs))
        return self.add_profile_loft(sections, cap_bottom=cap_bottom, cap_top=cap_top)

    def add_tube_along_path(self, path_pts, radii, rgba_u8, segs=10, closed_loop=False):
        """Egrisel demir halka, parmak koruyucu (knucklebow) ve yan siperler (Seitenringe) uretir."""
        count = len(path_pts)
        if count < 2:
            return []
        vecs = [Vector(p) for p in path_pts]
        ring_verts = []
        for i in range(count):
            if closed_loop:
                prev_v = vecs[(i - 1) % count]
                next_v = vecs[(i + 1) % count]
            else:
                prev_v = vecs[max(0, i - 1)]
                next_v = vecs[min(count - 1, i + 1)]
            tangent = (next_v - prev_v)
            if tangent.length < 1e-6:
                tangent = Vector((0, 0, 1))
            tangent.normalize()

            ref = Vector((0, 1, 0)) if abs(tangent.y) < 0.85 else Vector((1, 0, 0))
            side1 = tangent.cross(ref).normalized()
            side2 = tangent.cross(side1).normalized()
            rad = radii[i] if isinstance(radii, (list, tuple)) else radii

            rv = []
            for s in range(segs):
                a = (2.0 * math.pi * s) / segs
                pt = vecs[i] + (side1 * math.cos(a) + side2 * math.sin(a)) * rad
                rv.append(self.bm.verts.new(pt))
            ring_verts.append(rv)

        faces = []
        seg_count = count if closed_loop else count - 1
        for i in range(seg_count):
            r0 = ring_verts[i]
            r1 = ring_verts[(i + 1) % count]
            for s in range(segs):
                sn = (s + 1) % segs
                try:
                    f = self.bm.faces.new((r0[s], r0[sn], r1[sn], r1[s]))
                    faces.append(f)
                except ValueError:
                    pass

        if not closed_loop:
            try:
                faces.append(self.bm.faces.new(list(reversed(ring_verts[0]))))
            except ValueError:
                pass
            try:
                faces.append(self.bm.faces.new(ring_verts[-1]))
            except ValueError:
                pass

        bmesh.ops.recalc_face_normals(self.bm, faces=faces)
        self._paint_faces(faces, rgba_u8)
        return faces

    def add_tapered_bar_x(self, x_stops, rgba_u8):
        """
        X ekseni boyunca uzanan balcak/quillon/kanca kolu.
        x_stops: [(x, z_center, half_y, half_z, col_or_None), ...]
        """
        sections = []
        for entry in x_stops:
            x, zc, hy, hz = entry[0], entry[1], entry[2], entry[3]
            col = entry[4] if len(entry) > 4 and entry[4] is not None else rgba_u8
            # 8-gen pahli kesit (YZ duzleminde)
            c0 = 0.65
            pts = [
                (x, -hy, zc - hz * c0),
                (x, -hy * c0, zc - hz),
                (x, hy * c0, zc - hz),
                (x, hy, zc - hz * c0),
                (x, hy, zc + hz * c0),
                (x, hy * c0, zc + hz),
                (x, -hy * c0, zc + hz),
                (x, -hy, zc + hz * c0),
            ]
            sections.append((pts, [col] * 8))
        return self.add_profile_loft(sections, cap_bottom=True, cap_top=True)

    def finalize_and_export(self, out_glb_path: str):
        os.makedirs(os.path.dirname(out_glb_path), exist_ok=True)
        mesh = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(mesh)
        self.bm.free()

        # Keskin celik fasetleri + temiz normaller icin keskin kenarlara uyan duz/smooth ayari
        for poly in mesh.polygons:
            poly.use_smooth = False

        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        mat = create_weapon_preview_material()
        obj.data.materials.append(mat)

        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj

        bpy.ops.export_scene.gltf(
            filepath=out_glb_path,
            export_format="GLB",
            use_selection=True,
            export_yup=True,
            export_apply=True,
            export_normals=True,
            export_attributes=True,
            export_materials="NONE",
            export_texcoords=False,
            export_cameras=False,
            export_lights=False,
        )
        return obj


def make_fullered_blade_ring(z, half_w, fuller_w, ridge_t, fuller_t, x_off=0.0):
    """
    10 noktali gercekci oluklu (fullered) + keskin cift agizli celik namlu kesiti:
      0: -X keskin agiz
      1: -X pah sirti (-Y tarafi)
      2: -X fuller olugu tabani (-Y)
      3: +X fuller olugu tabani (-Y)
      4: +X pah sirti (-Y)
      5: +X keskin agiz
      6: +X pah sirti (+Y)
      7: +X fuller olugu tabani (+Y)
      8: -X fuller olugu tabani (+Y)
      9: -X pah sirti (+Y)
    """
    bevel_w = min(half_w * 0.56, max(fuller_w * 1.45, half_w * 0.40))
    pts = [
        (x_off - half_w, 0.0, z),
        (x_off - bevel_w, -ridge_t, z),
        (x_off - fuller_w, -fuller_t, z),
        (x_off + fuller_w, -fuller_t, z),
        (x_off + bevel_w, -ridge_t, z),
        (x_off + half_w, 0.0, z),
        (x_off + bevel_w, ridge_t, z),
        (x_off + fuller_w, fuller_t, z),
        (x_off - fuller_w, fuller_t, z),
        (x_off - bevel_w, ridge_t, z),
    ]
    edge_cols = [
        PAL["steel_edge"],    # 0->1 keskin pah
        PAL["steel_mid"],     # 1->2 oluk egimi
        PAL["steel_fuller"],  # 2->3 koyu fuller olugu
        PAL["steel_mid"],     # 3->4 oluk egimi
        PAL["steel_edge"],    # 4->5 keskin pah
        PAL["steel_edge"],    # 5->6 keskin pah
        PAL["steel_mid"],     # 6->7 oluk egimi
        PAL["steel_fuller"],  # 7->8 koyu fuller olugu
        PAL["steel_mid"],     # 8->9 oluk egimi
        PAL["steel_edge"],    # 9->0 keskin pah
    ]
    return pts, edge_cols


def make_diamond_blade_ring(z, half_w, ridge_t, x_off=0.0):
    """6 noktali keskin elmas/hex namlu kesiti (uc bolgesi veya rapier)."""
    mid_w = half_w * 0.22
    pts = [
        (x_off - half_w, 0.0, z),
        (x_off - mid_w, -ridge_t, z),
        (x_off + mid_w, -ridge_t, z),
        (x_off + half_w, 0.0, z),
        (x_off + mid_w, ridge_t, z),
        (x_off - mid_w, ridge_t, z),
    ]
    edge_cols = [
        PAL["steel_edge"],
        PAL["steel_bright"],
        PAL["steel_edge"],
        PAL["steel_edge"],
        PAL["steel_bright"],
        PAL["steel_edge"],
    ]
    return pts, edge_cols


def build_longsword():
    """1.85 kg Avrupa Uzun Kilici: Balcak alt yuzu y = 0.000m, kabza [-0.230..0.000], namlu [+0.020..+0.930]."""
    clear_scene()
    sb = SwordBuilder("Weapon_Longsword")

    # 1) Fasetli armut/tekerlek karisimi pirinc+celik topuz (pommel: z = -0.230 .. -0.180)
    sb.add_lathe_rings(
        [
            (-0.230, 0.006, 0.006, 0.0, 0.0, PAL["brass_dark"]),
            (-0.222, 0.017, 0.014, 0.0, 0.0, PAL["brass_main"]),
            (-0.205, 0.024, 0.018, 0.0, 0.0, PAL["brass_bright"]),
            (-0.190, 0.019, 0.015, 0.0, 0.0, PAL["brass_main"]),
            (-0.180, 0.012, 0.011, 0.0, 0.0, PAL["guard_steel"]),
        ],
        segs=10,
    )

    # 2) Bel kivrimli + nervurlu koyu kahve deri kabza (z = -0.180 .. 0.000)
    #    Sag el socket = -0.032, Sol el socket = -0.155 -> ikisi de tam deri kabzada!
    grip_rings = [
        (-0.180, 0.0125, 0.0110, 0.0, 0.0, PAL["leather_dark"]),
        (-0.155, 0.0138, 0.0120, 0.0, 0.0, PAL["leather_mid"]),
        (-0.125, 0.0152, 0.0130, 0.0, 0.0, PAL["leather_dark"]),
        (-0.095, 0.0168, 0.0142, 0.0, 0.0, PAL["brass_main"]),   # Orta pirinc sargi bilezigi
        (-0.088, 0.0168, 0.0142, 0.0, 0.0, PAL["brass_bright"]),
        (-0.082, 0.0156, 0.0132, 0.0, 0.0, PAL["leather_mid"]),
        (-0.055, 0.0152, 0.0128, 0.0, 0.0, PAL["leather_dark"]),
        (-0.028, 0.0158, 0.0134, 0.0, 0.0, PAL["leather_hi"]),
        (0.000, 0.0170, 0.0142, 0.0, 0.0, PAL["leather_dark"]),
    ]
    sb.add_lathe_rings(grip_rings, segs=12)

    # 3) Balcak (Crossguard: alt yuzu tam z = 0.000, ust yuzu z = +0.020, hafif one/yukari egimli zarif quillonlar)
    sb.add_tapered_bar_x(
        [
            (-0.132, 0.018, 0.0065, 0.0065, PAL["brass_bright"]),
            (-0.118, 0.013, 0.0055, 0.0060, PAL["guard_steel_hi"]),
            (-0.058, 0.010, 0.0082, 0.0088, PAL["guard_steel"]),
            (-0.022, 0.010, 0.0125, 0.0100, PAL["brass_main"]),
            (0.000, 0.010, 0.0135, 0.0100, PAL["brass_bright"]),
            (0.022, 0.010, 0.0125, 0.0100, PAL["brass_main"]),
            (0.058, 0.010, 0.0082, 0.0088, PAL["guard_steel"]),
            (0.118, 0.013, 0.0055, 0.0060, PAL["guard_steel_hi"]),
            (0.132, 0.018, 0.0065, 0.0065, PAL["brass_bright"]),
        ],
        PAL["guard_steel"],
    )

    # 4) Oluklu (Fullered) Avrupa Uzun Kilic Namlusu (z = +0.020 .. +0.760)
    fuller_sections = [
        make_fullered_blade_ring(0.020, half_w=0.0245, fuller_w=0.0062, ridge_t=0.0046, fuller_t=0.0026),
        make_fullered_blade_ring(0.160, half_w=0.0230, fuller_w=0.0058, ridge_t=0.0043, fuller_t=0.0024),
        make_fullered_blade_ring(0.360, half_w=0.0205, fuller_w=0.0050, ridge_t=0.0038, fuller_t=0.0022),
        make_fullered_blade_ring(0.560, half_w=0.0180, fuller_w=0.0038, ridge_t=0.0034, fuller_t=0.0023),
        make_fullered_blade_ring(0.680, half_w=0.0162, fuller_w=0.0018, ridge_t=0.0031, fuller_t=0.0028),
    ]
    sb.add_profile_loft(fuller_sections, cap_bottom=True, cap_top=False)

    # 5) Daralan Elmas Uc Govdesi (z = +0.680 .. +0.835) + Keskin Sivri Uc (+0.835 .. +0.935)
    tip_base_ring = make_diamond_blade_ring(0.680, half_w=0.0162, ridge_t=0.0031)
    tip_mid_ring = make_diamond_blade_ring(0.835, half_w=0.0115, ridge_t=0.0024)
    sb.add_profile_loft([tip_base_ring, tip_mid_ring], cap_bottom=False, cap_top=False)
    sb.add_point_tip(tip_mid_ring[0], (0.0, 0.0, 0.935), [PAL["steel_tip"]] * 6)

    out_path = os.path.join(OUT_WEAPONS_DIR, "weapon_longsword.glb")
    sb.finalize_and_export(out_path)
    return out_path


def build_katana():
    """
    1.35 kg Kavisli Samuray Katanasi:
      - Tsuba alt yuzu: z = 0.000m (ust yuzu +0.008m)
      - Tsuka (kabza): z = -0.235 .. 0.000m (rightY=-0.032, leftY=-0.185 tam tsuka uzerinde!)
      - Habaki pirinc bilezik: z = +0.008 .. +0.034m
      - Shinogi-zukuri kavisli namlu: z = +0.034 .. +0.865m (sori egrisi -X yonunde -> Unity'de +X egri!)
    """
    clear_scene()
    sb = SwordBuilder("Weapon_Katana")

    # 1) Kashira (alt pirinc/koyu celik kapak: z = -0.236 .. -0.214)
    sb.add_lathe_rings(
        [
            (-0.236, 0.010, 0.008, 0.0, 0.0, PAL["brass_dark"]),
            (-0.232, 0.0155, 0.0115, 0.0, 0.0, PAL["brass_main"]),
            (-0.216, 0.0158, 0.0118, 0.0, 0.0, PAL["guard_dark"]),
        ],
        segs=14,
    )

    # 2) Tsuka (oval kesitli samegawa + koyu tsuka-ito elmas sargi seritleri: z = -0.216 .. -0.014)
    tsuka_steps = 14
    z_start, z_end = -0.216, -0.014
    tsuka_rings = []
    for i in range(tsuka_steps + 1):
        t = i / tsuka_steps
        z = z_start + (z_end - z_start) * t
        # Hafif bel incelmesi + tsuka-ito sargi kabartisi (hishigami ritmi)
        waist = 1.0 - 0.07 * math.sin(t * math.pi)
        rib = 1.0 + (0.045 if (i % 2 == 0) else -0.035)
        rx = 0.0152 * waist * rib
        ry = 0.0112 * waist * rib
        col = PAL["tsuka_wrap"] if (i % 2 == 0) else (PAL["samegawa"] if i % 4 == 1 else PAL["tsuka_accent"])
        tsuka_rings.append((z, rx, ry, 0.0, 0.0, col))
    sb.add_lathe_rings(tsuka_rings, segs=14)

    # 3) Fuchi (tsuba alti bilezik: z = -0.014 .. 0.000)
    sb.add_lathe_rings(
        [
            (-0.014, 0.0165, 0.0124, 0.0, 0.0, PAL["guard_dark"]),
            (-0.002, 0.0172, 0.0130, 0.0, 0.0, PAL["brass_main"]),
            (0.000, 0.0172, 0.0130, 0.0, 0.0, PAL["brass_bright"]),
        ],
        segs=14,
    )

    # 4) Yuvarlak antik pirinc/altin Tsuba (alt yuzu tam z = 0.000, ust yuzu z = +0.008, kalin kenar cerceveli)
    sb.add_lathe_rings(
        [
            (0.000, 0.037, 0.035, 0.0, 0.0, PAL["brass_dark"]),
            (0.001, 0.041, 0.039, 0.0, 0.0, PAL["brass_main"]),
            (0.004, 0.042, 0.040, 0.0, 0.0, PAL["brass_bright"]),
            (0.007, 0.041, 0.039, 0.0, 0.0, PAL["brass_main"]),
            (0.008, 0.037, 0.035, 0.0, 0.0, PAL["brass_dark"]),
        ],
        segs=24,
    )
    # Tsuba ustu seppa pulu
    sb.add_lathe_rings(
        [
            (0.008, 0.019, 0.014, 0.0, 0.0, PAL["brass_bright"]),
            (0.011, 0.018, 0.013, 0.0, 0.0, PAL["brass_main"]),
        ],
        segs=14,
    )

    # 5) Habaki (pirinc namlu yakasi: z = +0.011 .. +0.034)
    def make_katana_ring(z, half_w, spine_t, sori_x, is_habaki=False):
        # Shinogi-zukuri tek agizli kesit:
        #   -X = keskin agiz (ha), +X = kalin sirt (mune), shinogi cikintisi +X tarafinda %35 geride
        edge_x = sori_x - half_w
        shinogi_x = sori_x + half_w * 0.25
        mune_x = sori_x + half_w
        pts = [
            (edge_x, 0.0, z),
            (shinogi_x, -spine_t, z),
            (mune_x, -spine_t * 0.72, z),
            (mune_x + half_w * 0.06, 0.0, z),
            (mune_x, spine_t * 0.72, z),
            (shinogi_x, spine_t, z),
        ]
        if is_habaki:
            cols = [PAL["brass_bright"], PAL["brass_main"], PAL["brass_dark"], PAL["brass_dark"], PAL["brass_main"], PAL["brass_bright"]]
        else:
            cols = [
                PAL["katana_hamon"],  # 0->1 parlak keskin hamon
                PAL["steel_mid"],     # 1->2 shinogi-ji ust yuzey
                PAL["katana_mune"],   # 2->3 koyu mune sirt
                PAL["katana_mune"],   # 3->4 koyu mune sirt
                PAL["steel_mid"],     # 4->5 shinogi-ji ust yuzey
                PAL["katana_hamon"],  # 5->0 parlak keskin hamon
            ]
        return pts, cols

    habaki_sections = [
        make_katana_ring(0.011, half_w=0.0168, spine_t=0.0052, sori_x=0.0, is_habaki=True),
        make_katana_ring(0.034, half_w=0.0160, spine_t=0.0046, sori_x=-0.0005, is_habaki=True),
    ]
    sb.add_profile_loft(habaki_sections, cap_bottom=True, cap_top=True)

    # 6) Kavisli Shinogi-Zukuri Katana Namlusu (z = +0.034 .. +0.810, zarif sori egrisi)
    blade_segs = 14
    z_b0, z_b1 = 0.034, 0.815
    blade_sections = []
    for i in range(blade_segs + 1):
        t = i / blade_segs
        z = z_b0 + (z_b1 - z_b0) * t
        # Parabolik torii-zori egrisi (sirt +X'e hafifce kavislenir -> Unity'de dogal sori)
        sori_x = (t * t) * 0.032
        hw = 0.0148 * (1.0 - 0.24 * t)
        st = 0.0038 * (1.0 - 0.28 * t)
        blade_sections.append(make_katana_ring(z, half_w=hw, spine_t=st, sori_x=sori_x))
    sb.add_profile_loft(blade_sections, cap_bottom=True, cap_top=False)

    # 7) Kissaki (egimli Japon kilic ucu: z = +0.815 .. +0.875)
    kissaki_mid = make_katana_ring(0.848, half_w=0.0076, spine_t=0.0021, sori_x=0.0365)
    sb.add_profile_loft([blade_sections[-1], kissaki_mid], cap_bottom=False, cap_top=False)
    sb.add_point_tip(kissaki_mid[0], (0.044, 0.0, 0.878), [PAL["steel_tip"]] * 6)

    out_path = os.path.join(OUT_WEAPONS_DIR, "weapon_katana.glb")
    sb.finalize_and_export(out_path)
    return out_path


def build_zweihander():
    """
    5.60 kg Alman Zweihander (Greatsword):
      - Balcak alt yuzu: z = 0.000m (ust yuzu +0.020m)
      - Uzun cift-el deri kabza: z = -0.255 .. 0.000m (rightY=-0.036, leftY=-0.225 tam kabzada!)
      - Fasetli scent-stopper topuz: z = -0.320 .. -0.255m
      - Yan koruma halkalari (Seitenringe) + genis acili quillonlar
      - Deri sargili Ricasso: z = +0.020 .. +0.195m (Half-Sword [X] sol el = +0.135m tam ricasso ortasi!)
      - Parierhaken yan savunma kancalari: z = +0.190 .. +0.248m
      - Ince 6-gen/fuller celik namlu: z = +0.195 .. +1.125m
    """
    clear_scene()
    sb = SwordBuilder("Weapon_Zweihander")

    # 1) Fasetli armut (scent-stopper) topuz (z = -0.320 .. -0.255)
    sb.add_lathe_rings(
        [
            (-0.320, 0.005, 0.005, 0.0, 0.0, PAL["brass_dark"]),
            (-0.312, 0.016, 0.015, 0.0, 0.0, PAL["brass_main"]),
            (-0.288, 0.025, 0.022, 0.0, 0.0, PAL["brass_bright"]),
            (-0.268, 0.019, 0.017, 0.0, 0.0, PAL["brass_main"]),
            (-0.255, 0.013, 0.012, 0.0, 0.0, PAL["guard_steel"]),
        ],
        segs=10,
    )

    # 2) Uzun cift-el bel kivrimli deri kabza + orta pirinc bilezik (z = -0.255 .. 0.000)
    sb.add_lathe_rings(
        [
            (-0.255, 0.0135, 0.0120, 0.0, 0.0, PAL["leather_dark"]),
            (-0.225, 0.0148, 0.0130, 0.0, 0.0, PAL["leather_mid"]),
            (-0.180, 0.0162, 0.0142, 0.0, 0.0, PAL["leather_dark"]),
            (-0.136, 0.0178, 0.0156, 0.0, 0.0, PAL["brass_main"]),
            (-0.126, 0.0178, 0.0156, 0.0, 0.0, PAL["brass_bright"]),
            (-0.118, 0.0164, 0.0144, 0.0, 0.0, PAL["leather_hi"]),
            (-0.075, 0.0158, 0.0138, 0.0, 0.0, PAL["leather_mid"]),
            (-0.036, 0.0160, 0.0140, 0.0, 0.0, PAL["leather_dark"]),
            (0.000, 0.0176, 0.0152, 0.0, 0.0, PAL["leather_dark"]),
        ],
        segs=12,
    )

    # 3) Genis ince balcak (alt yuzu tam z = 0.000, ust yuzu z = +0.020) + yukari kivrik quillon uclari
    sb.add_tapered_bar_x(
        [
            (-0.195, 0.044, 0.0055, 0.0055, PAL["brass_bright"]),
            (-0.172, 0.022, 0.0068, 0.0068, PAL["guard_steel_hi"]),
            (-0.125, 0.011, 0.0078, 0.0085, PAL["guard_steel"]),
            (-0.042, 0.010, 0.0110, 0.0100, PAL["guard_steel"]),
            (0.000, 0.010, 0.0135, 0.0100, PAL["brass_main"]),
            (0.042, 0.010, 0.0110, 0.0100, PAL["guard_steel"]),
            (0.125, 0.011, 0.0078, 0.0085, PAL["guard_steel"]),
            (0.172, 0.022, 0.0068, 0.0068, PAL["guard_steel_hi"]),
            (0.195, 0.044, 0.0055, 0.0055, PAL["brass_bright"]),
        ],
        PAL["guard_steel"],
    )

    # Yan koruma halkalari (Seitenringe — on ve arka yuzde dairesel celik/pirinc halkalar)
    for y_sign in (-1.0, 1.0):
        ring_pts = []
        for s in range(14):
            a = (2.0 * math.pi * s) / 14
            rx = math.cos(a) * 0.044
            ry = y_sign * (0.012 + (0.5 - 0.5 * math.cos(a * 2.0)) * 0.014 + 0.016)
            rz = 0.010 + math.sin(a) * 0.012
            ring_pts.append((rx, ry, rz))
        sb.add_tube_along_path(ring_pts, radii=0.0046, rgba_u8=PAL["brass_main"], segs=8, closed_loop=True)

    # 4) Deri sargili ince Ricasso (z = +0.020 .. +0.195 — [X] Half-Sword sol el +0.135m!)
    def make_ricasso_ring(z, hw, ht, col_face, col_edge):
        pts = [
            (-hw, 0.0, z),
            (-hw * 0.62, -ht, z),
            (hw * 0.62, -ht, z),
            (hw, 0.0, z),
            (hw * 0.62, ht, z),
            (-hw * 0.62, ht, z),
        ]
        cols = [col_edge, col_face, col_edge, col_edge, col_face, col_edge]
        return pts, cols

    sb.add_profile_loft(
        [
            make_ricasso_ring(0.020, 0.0210, 0.0072, PAL["leather_dark"], PAL["guard_steel"]),
            make_ricasso_ring(0.075, 0.0202, 0.0070, PAL["leather_mid"], PAL["guard_steel"]),
            make_ricasso_ring(0.135, 0.0196, 0.0068, PAL["leather_hi"], PAL["guard_steel_hi"]),
            make_ricasso_ring(0.195, 0.0200, 0.0068, PAL["leather_dark"], PAL["guard_steel"]),
        ],
        cap_bottom=True,
        cap_top=False,
    )

    # 5) Parierhaken (iki yana yukari egimli Alman savunma kancalari: z = +0.188 .. +0.248)
    for x_sign in (-1.0, 1.0):
        sb.add_tapered_bar_x(
            [
                (x_sign * 0.016, 0.198, 0.0065, 0.0095, PAL["guard_steel"]),
                (x_sign * 0.048, 0.214, 0.0052, 0.0075, PAL["guard_steel_hi"]),
                (x_sign * 0.084, 0.244, 0.0032, 0.0040, PAL["steel_edge"]),
            ],
            PAL["guard_steel_hi"],
        )

    # 6) Ince 6-gen/oluklu Zweihander celik namlusu (z = +0.195 .. +0.960)
    zw_sections = [
        make_fullered_blade_ring(0.195, half_w=0.0275, fuller_w=0.0072, ridge_t=0.0052, fuller_t=0.0028),
        make_fullered_blade_ring(0.420, half_w=0.0245, fuller_w=0.0062, ridge_t=0.0046, fuller_t=0.0025),
        make_fullered_blade_ring(0.660, half_w=0.0208, fuller_w=0.0048, ridge_t=0.0040, fuller_t=0.0024),
        make_fullered_blade_ring(0.840, half_w=0.0176, fuller_w=0.0024, ridge_t=0.0035, fuller_t=0.0029),
    ]
    sb.add_profile_loft(zw_sections, cap_bottom=True, cap_top=False)

    # 7) Keskin daralan elmas mizrak ucu (z = +0.840 .. +1.120)
    zw_tip0 = make_diamond_blade_ring(0.840, half_w=0.0176, ridge_t=0.0035)
    zw_tip1 = make_diamond_blade_ring(0.990, half_w=0.0124, ridge_t=0.0027)
    sb.add_profile_loft([zw_tip0, zw_tip1], cap_bottom=False, cap_top=False)
    sb.add_point_tip(zw_tip1[0], (0.0, 0.0, 1.120), [PAL["steel_tip"]] * 6)

    out_path = os.path.join(OUT_WEAPONS_DIR, "weapon_zweihander.glb")
    sb.finalize_and_export(out_path)
    return out_path


def build_rapier():
    """
    0.95 kg Ronesans Eskrim Kilici (Rapier):
      - Balcak / canak siper alt yuzu: z = 0.000m (el siperi canagi sag eli -0.042m etrafinda zarifce sarar!)
      - Kabza: z = -0.118 .. 0.000m (rightY = -0.042m canak ici)
      - Fasetli damla topuz: z = -0.158 .. -0.118m
      - Suslu canak + kafes el siperi (knucklebow + yan halkalar + uzun ince quillonlar)
      - Ince elmas kesitli delici namlu: z = +0.020 .. +1.010m
    """
    clear_scene()
    sb = SwordBuilder("Weapon_Rapier")

    # 1) Fasetli damla topuz (z = -0.158 .. -0.118)
    sb.add_lathe_rings(
        [
            (-0.158, 0.004, 0.004, 0.0, 0.0, PAL["brass_dark"]),
            (-0.148, 0.015, 0.015, 0.0, 0.0, PAL["brass_main"]),
            (-0.134, 0.018, 0.018, 0.0, 0.0, PAL["brass_bright"]),
            (-0.118, 0.010, 0.010, 0.0, 0.0, PAL["brass_dark"]),
        ],
        segs=12,
    )

    # 2) Ince tel sargili bordo eskrim kabzasi (z = -0.118 .. 0.000)
    rapier_grip = []
    for i in range(9):
        t = i / 8.0
        z = -0.118 + 0.118 * t
        sw = 1.0 + 0.14 * math.sin(t * math.pi)
        col = PAL["rapier_wire"] if (i % 3 == 0) else PAL["rapier_grip"]
        rapier_grip.append((z, 0.0112 * sw, 0.0102 * sw, 0.0, 0.0, col))
    sb.add_lathe_rings(rapier_grip, segs=12)

    # 3) Canak Siper (Cup Hilt: ust kapa z = +0.018, canak agzi z = -0.048'e dogru acilir; ici bos kabuk!)
    cup_segs = 18
    cup_profile = [
        # (z, outer_r, inner_r, col_out, col_in)
        (0.018, 0.018, 0.010, PAL["brass_bright"], PAL["rapier_cup_inner"]),
        (0.008, 0.036, 0.030, PAL["rapier_cup"], PAL["rapier_cup_inner"]),
        (-0.010, 0.052, 0.047, PAL["rapier_cup"], PAL["rapier_cup_inner"]),
        (-0.032, 0.060, 0.055, PAL["rapier_cup"], PAL["rapier_cup_inner"]),
        (-0.048, 0.063, 0.058, PAL["brass_main"], PAL["brass_dark"]),
    ]
    # Dis yuzey
    outer_secs = []
    for z, r_out, _, col_out, _ in cup_profile:
        pts = [(math.cos(2.0 * math.pi * s / cup_segs) * r_out, math.sin(2.0 * math.pi * s / cup_segs) * r_out, z) for s in range(cup_segs)]
        outer_secs.append((pts, [col_out] * cup_segs))
    sb.add_profile_loft(outer_secs, cap_bottom=False, cap_top=True)

    # Ic yuzey (ters sira ki normaller ice baksin)
    inner_secs = []
    for z, _, r_in, _, col_in in reversed(cup_profile):
        pts = [(math.cos(2.0 * math.pi * s / cup_segs) * r_in, math.sin(2.0 * math.pi * s / cup_segs) * r_in, z) for s in range(cup_segs)]
        inner_secs.append((pts, [col_in] * cup_segs))
    sb.add_profile_loft(inner_secs, cap_bottom=False, cap_top=False)

    # Canak kenar bilezigi (rolled rim)
    rim_pts = [(math.cos(2.0 * math.pi * s / cup_segs) * 0.0605, math.sin(2.0 * math.pi * s / cup_segs) * 0.0605, -0.048) for s in range(cup_segs)]
    sb.add_tube_along_path(rim_pts, radii=0.0042, rgba_u8=PAL["brass_bright"], segs=8, closed_loop=True)

    # 4) Uzun ince Quillon capraz balcak (alt yuzu z = 0.000, +0.012 ustu)
    sb.add_tapered_bar_x(
        [
            (-0.138, 0.008, 0.0042, 0.0042, PAL["brass_bright"]),
            (-0.125, 0.006, 0.0030, 0.0032, PAL["brass_main"]),
            (-0.045, 0.006, 0.0042, 0.0055, PAL["brass_main"]),
            (0.000, 0.006, 0.0075, 0.0060, PAL["brass_bright"]),
            (0.045, 0.006, 0.0042, 0.0055, PAL["brass_main"]),
            (0.125, 0.006, 0.0030, 0.0032, PAL["brass_main"]),
            (0.138, 0.008, 0.0042, 0.0042, PAL["brass_bright"]),
        ],
        PAL["brass_main"],
    )

    # 5) Parmak koruma kemeri (Knucklebow: quillon -> parmak onunden topuza) + 2 yan kafes yayi
    bow_pts = [
        (0.000, 0.000, 0.006),
        (0.042, 0.000, -0.008),
        (0.064, 0.000, -0.042),
        (0.062, 0.000, -0.088),
        (0.044, 0.000, -0.118),
        (0.014, 0.000, -0.132),
    ]
    sb.add_tube_along_path(bow_pts, radii=0.0036, rgba_u8=PAL["brass_bright"], segs=8, closed_loop=False)

    for y_sign in (-1.0, 1.0):
        cage_pts = [
            (0.012, y_sign * 0.036, -0.004),
            (0.046, y_sign * 0.044, -0.038),
            (0.054, y_sign * 0.024, -0.072),
            (0.048, y_sign * 0.004, -0.096),
        ]
        sb.add_tube_along_path(cage_pts, radii=0.0028, rgba_u8=PAL["brass_main"], segs=8, closed_loop=False)

    # 6) Ince delici elmas namlu (z = +0.020 .. +1.015)
    rap_sections = [
        make_diamond_blade_ring(0.020, half_w=0.0112, ridge_t=0.0044),
        make_diamond_blade_ring(0.140, half_w=0.0098, ridge_t=0.0040),
        make_diamond_blade_ring(0.420, half_w=0.0078, ridge_t=0.0034),
        make_diamond_blade_ring(0.720, half_w=0.0056, ridge_t=0.0026),
        make_diamond_blade_ring(0.920, half_w=0.0034, ridge_t=0.0018),
    ]
    sb.add_profile_loft(rap_sections, cap_bottom=True, cap_top=False)
    sb.add_point_tip(rap_sections[-1][0], (0.0, 0.0, 1.015), [PAL["steel_tip"]] * 6)

    out_path = os.path.join(OUT_WEAPONS_DIR, "weapon_rapier.glb")
    sb.finalize_and_export(out_path)
    return out_path


def main():
    os.makedirs(OUT_WEAPONS_DIR, exist_ok=True)
    builders = [
        ("Longsword", build_longsword),
        ("Katana", build_katana),
        ("Zweihander", build_zweihander),
        ("Rapier", build_rapier),
    ]
    for label, fn in builders:
        path = fn()
        size_kb = os.path.getsize(path) / 1024.0
        print(f"[WeaponForgedGlbBuilder] Built {label}: {path} ({size_kb:.1f} KB)")


if __name__ == "__main__":
    main()
