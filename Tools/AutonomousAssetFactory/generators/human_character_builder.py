"""
Stratocraft Otonom 3B Fabrika — Gercekci Eklemli Insan Karakteri Ureticisi (Blender 4.2).

Kullanim:
    Tools/bin/blender-4.2/blender.exe --background --python \
        Tools/AutonomousAssetFactory/generators/human_character_builder.py

Ciktilar:
    - Assets/Art/Generated/Character/player_human_articulated.glb (0 texture, RGB COLOR_0)
    - BuildLogs/Previews/player_human_articulated_contact_sheet.png (4 acili denetim karti)
"""

import math
import os

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, "..", "..", ".."))
OUT_GLB = os.path.join(REPO_ROOT, "Assets", "Art", "Generated", "Character", "player_human_articulated.glb")
OUT_PREVIEW_DIR = os.path.join(REPO_ROOT, "BuildLogs", "Previews")
OUT_CONTACT_SHEET = os.path.join(OUT_PREVIEW_DIR, "player_human_articulated_contact_sheet.png")


# Dogal insan ve orta-cag maceraci renk paleti (sRGB 0..255 -> glTF COLOR_0)
PAL = {
    "skin": (235, 188, 154, 255),
    "skin_shade": (210, 160, 126, 255),
    "skin_blush": (230, 172, 144, 255),
    "lips": (184, 118, 106, 255),
    "sclera": (242, 244, 246, 255),
    "iris": (54, 118, 168, 255),
    "pupil": (20, 22, 28, 255),
    "eyebrow": (64, 40, 24, 255),
    "hair_base": (86, 52, 30, 255),
    "hair_light": (116, 74, 44, 255),
    "hair_dark": (58, 34, 20, 255),
    "tunic": (62, 102, 128, 255),
    "tunic_trim": (194, 156, 84, 255),
    "undershirt": (224, 214, 196, 255),
    "leather": (122, 74, 42, 255),
    "leather_dark": (84, 50, 28, 255),
    "brass": (214, 172, 74, 255),
    "trousers": (76, 74, 82, 255),
    "trousers_pad": (106, 68, 42, 255),
    "boot": (102, 64, 38, 255),
    "boot_sole": (44, 36, 32, 255),
}


def srgb_to_linear_rgba(rgba_u8):
    """Blender 4.2 BYTE_COLOR attribute -> glTF COLOR_0 icin linear float4 donusumu."""

    def _c(v):
        x = max(0.0, min(1.0, v / 255.0))
        return x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4

    return (_c(rgba_u8[0]), _c(rgba_u8[1]), _c(rgba_u8[2]), rgba_u8[3] / 255.0)


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def create_vertex_color_material():
    mat = bpy.data.materials.new(name="M_HumanVertexColor")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    vcol = nt.nodes.new("ShaderNodeVertexColor")
    vcol.layer_name = "Col"
    bsdf.inputs["Roughness"].default_value = 0.68
    bsdf.inputs["Specular IOR Level"].default_value = 0.28
    nt.links.new(vcol.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


class PartBuilder:
    """Tek bir eklem parcasinin bmesh insa yardimcisi (yerel pivot = (0,0,0), -Y = on, +Z = yukari)."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.color_layer = self.bm.loops.layers.color.new("Col")

    def _paint_faces(self, faces, rgba_u8, shade_grad=0.0):
        lin = srgb_to_linear_rgba(rgba_u8)
        for f in faces:
            z_factor = 1.0
            if shade_grad != 0.0:
                cz = f.calc_center_median().z
                z_factor = max(0.75, min(1.18, 1.0 + cz * shade_grad))
            col = (
                min(1.0, lin[0] * z_factor),
                min(1.0, lin[1] * z_factor),
                min(1.0, lin[2] * z_factor),
                lin[3],
            )
            for loop in f.loops:
                loop[self.color_layer] = col

    def add_loft(
        self, rings, rgba_u8, cap_bottom=True, cap_top=True, rot_euler=(0, 0, 0), offset=(0, 0, 0)
    ):
        """
        Anatomik profil halkalarindan (rings: [(z, rx, ry, cx, cy), ...]) organik silindir/govde uretir.
        """
        segs = 14
        rot_m = Euler(rot_euler, "XYZ").to_matrix().to_4x4()
        off_v = Vector(offset)
        ring_verts = []
        created_faces = []
        for z, rx, ry, cx, cy in rings:
            rv = []
            for s in range(segs):
                a = (2.0 * math.pi * s) / segs
                p = Vector((cx + math.cos(a) * rx, cy + math.sin(a) * ry, z))
                p = (rot_m @ p) + off_v
                rv.append(self.bm.verts.new(p))
            ring_verts.append(rv)

        for r in range(len(ring_verts) - 1):
            r0 = ring_verts[r]
            r1 = ring_verts[r + 1]
            for s in range(segs):
                sn = (s + 1) % segs
                try:
                    f = self.bm.faces.new((r0[s], r0[sn], r1[sn], r1[s]))
                    created_faces.append(f)
                except ValueError:
                    pass

        if cap_bottom and len(ring_verts) > 0:
            try:
                f = self.bm.faces.new(list(reversed(ring_verts[0])))
                created_faces.append(f)
            except ValueError:
                pass
        if cap_top and len(ring_verts) > 0:
            try:
                f = self.bm.faces.new(ring_verts[-1])
                created_faces.append(f)
            except ValueError:
                pass

        bmesh.ops.recalc_face_normals(self.bm, faces=created_faces)
        self._paint_faces(created_faces, rgba_u8)
        return created_faces

    def add_ellipsoid(self, center, radii, rgba_u8, segs=12, rings=8, rot_euler=(0, 0, 0)):
        rot_m = Euler(rot_euler, "XYZ").to_matrix().to_4x4()
        scale_m = Matrix.Diagonal((radii[0], radii[1], radii[2], 1.0))
        xform = Matrix.Translation(Vector(center)) @ rot_m @ scale_m
        ret = bmesh.ops.create_uvsphere(
            self.bm, u_segments=segs, v_segments=rings, radius=1.0, matrix=xform
        )
        verts = set(ret["verts"])
        faces = [f for f in self.bm.faces if all(v in verts for v in f.verts)]
        bmesh.ops.recalc_face_normals(self.bm, faces=faces)
        self._paint_faces(faces, rgba_u8)
        return faces

    def add_box(self, center, half_extents, rgba_u8, rot_euler=(0, 0, 0), bevel_amt=0.0):
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
        self._paint_faces(new_faces, rgba_u8)
        return new_faces

    def finalize_object(self, mat, world_pos=(0, 0, 0), smooth_shading=True):
        # Temiz triangulasyon + dis normal dogrulamasi
        bmesh.ops.triangulate(self.bm, faces=self.bm.faces[:])
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces[:])

        mesh = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(mesh)
        self.bm.free()

        for p in mesh.polygons:
            p.use_smooth = smooth_shading

        obj = bpy.data.objects.new(self.name, mesh)
        obj.location = Vector(world_pos)
        obj.data.materials.append(mat)
        bpy.context.collection.objects.link(obj)
        return obj

    def add_curved_tube(self, stations, segs=16, cap_start=True, cap_end=True):
        """
        3B egri uzerindeki istasyonlardan (center, rx, ry, groove_amp, rgba_u8, axis_x_hint)
        tek parca, kesisimsiz, su gecirmez organik loft kabuk uretir (bogumsuz gercek insan eli/basparmak).
        """
        ring_verts = []
        ring_colors = []
        count = len(stations)
        for i, st in enumerate(stations):
            center = Vector(st[0])
            rx, ry, groove, rgba = st[1], st[2], st[3], st[4]
            ax_hint = Vector(st[5]) if len(st) > 5 else Vector((1.0, 0.0, 0.0))

            if i == 0:
                tangent = Vector(stations[1][0]) - center
            elif i == count - 1:
                tangent = center - Vector(stations[i - 1][0])
            else:
                tangent = Vector(stations[i + 1][0]) - Vector(stations[i - 1][0])
            if tangent.length < 1e-6:
                tangent = Vector((0.0, 0.0, -1.0))
            tangent.normalize()

            axis_x = ax_hint - tangent * ax_hint.dot(tangent)
            if axis_x.length < 1e-5:
                axis_x = Vector((1.0, 0.0, 0.0))
            axis_x.normalize()
            axis_n = tangent.cross(axis_x).normalized()

            rv = []
            for s in range(segs):
                a = (2.0 * math.pi * s) / segs
                ca = math.cos(a)
                sa = math.sin(a)
                # Superellipse yassi-organik insan avuc/parmak kesiti (yuvarlak sosis degil!)
                sx = math.copysign(abs(ca) ** 0.78, ca) * rx
                sn = math.copysign(abs(sa) ** 0.86, sa) * ry
                if groove > 0.0 and sa > 0.05:
                    # Dis sirt yuzeyinde 4 bitisik parmak olugu (tek parca mesh, sifir kesisim!)
                    valley = 0.5 - 0.5 * math.cos(3.0 * math.pi * ca)
                    sn -= groove * valley * (sa**1.2)
                pt = center + axis_x * sx + axis_n * sn
                rv.append(self.bm.verts.new(pt))
            ring_verts.append(rv)
            ring_colors.append(rgba)

        all_faces = []
        for r in range(count - 1):
            r0 = ring_verts[r]
            r1 = ring_verts[r + 1]
            band_faces = []
            for s in range(segs):
                sn_idx = (s + 1) % segs
                try:
                    f = self.bm.faces.new((r0[s], r0[sn_idx], r1[sn_idx], r1[s]))
                    band_faces.append(f)
                    all_faces.append(f)
                except ValueError:
                    pass
            bmesh.ops.recalc_face_normals(self.bm, faces=band_faces)
            self._paint_faces(band_faces, ring_colors[r + 1])

        if cap_start and count > 0:
            try:
                f0 = self.bm.faces.new(list(reversed(ring_verts[0])))
                bmesh.ops.recalc_face_normals(self.bm, faces=[f0])
                self._paint_faces([f0], ring_colors[0])
                all_faces.append(f0)
            except ValueError:
                pass
        if cap_end and count > 0:
            try:
                f1 = self.bm.faces.new(ring_verts[-1])
                bmesh.ops.recalc_face_normals(self.bm, faces=[f1])
                self._paint_faces([f1], ring_colors[-1])
                all_faces.append(f1)
            except ValueError:
                pass
        return all_faces


def build_pelvis(mat):
    pb = PartBuilder("Human_Pelvis")
    # Pantolon ust kalca & bel profili (pivot = kalca merkezi z=0)
    pb.add_loft(
        [
            (-0.085, 0.145, 0.095, 0.0, 0.005),
            (-0.040, 0.165, 0.112, 0.0, 0.004),
            (0.015, 0.162, 0.108, 0.0, 0.002),
            (0.068, 0.148, 0.098, 0.0, 0.000),
        ],
        PAL["trousers"],
    )
    # Deri kemer + pirinc toka + yan kese
    pb.add_loft(
        [
            (0.022, 0.168, 0.114, 0.0, 0.001),
            (0.068, 0.162, 0.110, 0.0, 0.000),
        ],
        PAL["leather"],
    )
    pb.add_box((0.0, -0.114, 0.045), (0.032, 0.014, 0.026), PAL["brass"], bevel_amt=0.004)
    pb.add_box((-0.162, -0.020, 0.012), (0.026, 0.042, 0.046), PAL["leather_dark"], bevel_amt=0.006)
    return pb.finalize_object(mat, world_pos=(0.0, 0.0, 0.92))


def build_torso(mat):
    pb = PartBuilder("Human_Torso")
    # Belden omuzlara anatomik insan govdesi (tunik)
    pb.add_loft(
        [
            (0.000, 0.146, 0.096, 0.0, 0.000),
            (0.080, 0.142, 0.098, 0.0, -0.004),
            (0.180, 0.168, 0.114, 0.0, -0.006),
            (0.295, 0.194, 0.124, 0.0, -0.008),
            (0.395, 0.210, 0.118, 0.0, -0.004),
            (0.455, 0.168, 0.092, 0.0, 0.004),
            (0.482, 0.072, 0.064, 0.0, 0.006),
        ],
        PAL["tunic"],
    )
    # Tunik etek ucu + altin serit
    pb.add_loft(
        [
            (-0.045, 0.166, 0.112, 0.0, 0.000),
            (0.015, 0.156, 0.104, 0.0, 0.000),
        ],
        PAL["tunic"],
    )
    pb.add_loft(
        [
            (-0.048, 0.168, 0.114, 0.0, 0.000),
            (-0.028, 0.167, 0.113, 0.0, 0.000),
        ],
        PAL["tunic_trim"],
    )
    # Acik yaka ic gomlek & gercek insan teni gogus/koprucuk V-acikligi
    pb.add_ellipsoid((0.0, -0.092, 0.410), (0.072, 0.036, 0.068), PAL["undershirt"], segs=10, rings=6)
    pb.add_ellipsoid((0.0, -0.088, 0.445), (0.048, 0.034, 0.042), PAL["skin"], segs=10, rings=6)
    # Katlanmis yaka kenarlari (sol/sag)
    pb.add_box(
        (-0.078, -0.072, 0.442),
        (0.036, 0.038, 0.024),
        PAL["tunic_trim"],
        rot_euler=(0.25, 0.35, -0.45),
        bevel_amt=0.005,
    )
    pb.add_box(
        (0.078, -0.072, 0.442),
        (0.036, 0.038, 0.024),
        PAL["tunic_trim"],
        rot_euler=(0.25, -0.35, 0.45),
        bevel_amt=0.005,
    )
    # Capraz deri omuz kayisi + pirinc toka (govdeye tam oturan egim)
    pb.add_box(
        (0.0, -0.106, 0.240),
        (0.022, 0.012, 0.215),
        PAL["leather"],
        rot_euler=(-0.06, 0.54, 0.0),
        bevel_amt=0.003,
    )
    pb.add_box(
        (0.0, 0.104, 0.240),
        (0.022, 0.012, 0.215),
        PAL["leather"],
        rot_euler=(0.04, -0.54, 0.0),
        bevel_amt=0.003,
    )
    pb.add_box(
        (-0.052, -0.118, 0.312),
        (0.020, 0.010, 0.022),
        PAL["brass"],
        rot_euler=(-0.04, 0.50, 0.0),
        bevel_amt=0.003,
    )
    return pb.finalize_object(mat, world_pos=(0.0, 0.0, 0.98))


def build_neck(mat):
    pb = PartBuilder("Human_Neck")
    pb.add_loft(
        [
            (-0.015, 0.058, 0.056, 0.0, 0.004),
            (0.035, 0.052, 0.052, 0.0, 0.002),
            (0.088, 0.055, 0.055, 0.0, 0.000),
        ],
        PAL["skin"],
    )
    return pb.finalize_object(mat, world_pos=(0.0, 0.004, 1.455))


def build_head(mat):
    """Yuz, gozler, kaslar, burun, dudaklar, kulaklar ve katmanli sacli gercek insan kafasi."""
    pb = PartBuilder("Human_Head")

    # 1. Anatomik kafatasi + cene + yanak profili (z = 0.0 cene alti .. 0.245 tepe)
    pb.add_loft(
        [
            (0.008, 0.036, 0.044, 0.0, -0.024),  # Cene ucu
            (0.036, 0.068, 0.082, 0.0, -0.014),  # Alt cene (mandible)
            (0.078, 0.082, 0.096, 0.0, -0.006),  # Yanak / agiz hizasi
            (0.126, 0.088, 0.102, 0.0, -0.002),  # Elmacik kemigi / goz hizasi
            (0.172, 0.089, 0.104, 0.0, 0.004),  # Alin / sakak
            (0.214, 0.076, 0.088, 0.0, 0.008),  # Kafatasi ustu
            (0.238, 0.042, 0.050, 0.0, 0.010),  # Tepe
        ],
        PAL["skin"],
    )

    # 2. Kulaklar (sag = -X, sol = +X, on = -Y)
    for side in (-1.0, 1.0):
        pb.add_ellipsoid(
            (side * 0.088, 0.008, 0.116),
            (0.014, 0.022, 0.032),
            PAL["skin_shade"],
            segs=8,
            rings=6,
            rot_euler=(0.0, 0.0, side * 0.25),
        )

    # 3. Burun (kopru + uc + burun kanatlari)
    pb.add_box(
        (0.0, -0.104, 0.114),
        (0.012, 0.018, 0.024),
        PAL["skin"],
        rot_euler=(-0.22, 0.0, 0.0),
        bevel_amt=0.004,
    )
    pb.add_ellipsoid((0.0, -0.114, 0.096), (0.016, 0.015, 0.012), PAL["skin_shade"], segs=8, rings=5)

    # 4. Gozler (goz cukuru + beyaz sklera + mavi/ela iris + siyah gozbebegi + ust goz kapagi + kaslar)
    for side in (-1.0, 1.0):
        ex = side * 0.034
        # Goz cukuru hafif golge
        pb.add_ellipsoid((ex, -0.093, 0.130), (0.021, 0.010, 0.014), PAL["skin_shade"], segs=8, rings=5)
        # Beyaz goz aki (sclera)
        pb.add_ellipsoid((ex, -0.099, 0.130), (0.016, 0.008, 0.010), PAL["sclera"], segs=10, rings=6)
        # Iris
        pb.add_ellipsoid((ex, -0.105, 0.130), (0.009, 0.005, 0.009), PAL["iris"], segs=8, rings=5)
        # Gozbebegi (pupil)
        pb.add_ellipsoid((ex, -0.109, 0.130), (0.0048, 0.003, 0.0048), PAL["pupil"], segs=8, rings=4)
        # Ust goz kapagi cizgisi
        pb.add_box(
            (ex, -0.101, 0.139), (0.017, 0.008, 0.0032), PAL["eyebrow"], rot_euler=(0.0, side * 0.08, 0.0)
        )
        # Kas (eyebrow)
        pb.add_box(
            (ex, -0.101, 0.149),
            (0.021, 0.008, 0.0045),
            PAL["eyebrow"],
            rot_euler=(0.0, -side * 0.10, side * 0.08),
            bevel_amt=0.0015,
        )

    # 5. Agiz ve Dudaklar (ust dudak + alt dudak)
    pb.add_ellipsoid((0.0, -0.096, 0.063), (0.022, 0.008, 0.0052), PAL["lips"], segs=8, rings=5)
    pb.add_ellipsoid((0.0, -0.094, 0.056), (0.018, 0.008, 0.0050), PAL["lips"], segs=8, rings=5)

    # 6. Katmanli Organik Insan Saci (tepe + arka + yan favoriler + organik taranmis on percem)
    pb.add_ellipsoid((0.0, 0.014, 0.180), (0.098, 0.114, 0.082), PAL["hair_base"], segs=14, rings=8)
    pb.add_ellipsoid((0.0, 0.058, 0.112), (0.086, 0.068, 0.084), PAL["hair_dark"], segs=12, rings=6)
    pb.add_ellipsoid(
        (0.0, 0.084, 0.058),
        (0.060, 0.036, 0.048),
        PAL["hair_dark"],
        segs=10,
        rings=5,
        rot_euler=(0.24, 0.0, 0.0),
    )
    pb.add_ellipsoid(
        (-0.024, -0.016, 0.226),
        (0.064, 0.082, 0.034),
        PAL["hair_light"],
        segs=10,
        rings=6,
        rot_euler=(0.12, 0.16, 0.10),
    )
    pb.add_ellipsoid(
        (0.030, -0.008, 0.218),
        (0.058, 0.076, 0.032),
        PAL["hair_base"],
        segs=10,
        rings=6,
        rot_euler=(0.10, -0.16, -0.10),
    )
    # Organik taranmis percem tutamlari
    pb.add_ellipsoid(
        (-0.046, -0.082, 0.182),
        (0.036, 0.026, 0.024),
        PAL["hair_light"],
        segs=10,
        rings=5,
        rot_euler=(0.30, 0.22, -0.28),
    )
    pb.add_ellipsoid(
        (0.002, -0.090, 0.186),
        (0.042, 0.026, 0.022),
        PAL["hair_base"],
        segs=10,
        rings=5,
        rot_euler=(0.35, -0.12, 0.10),
    )
    pb.add_ellipsoid(
        (0.048, -0.080, 0.180),
        (0.036, 0.026, 0.024),
        PAL["hair_light"],
        segs=10,
        rings=5,
        rot_euler=(0.28, -0.22, 0.28),
    )
    # Yan favoriler (organik ellipsoid)
    for side in (-1.0, 1.0):
        pb.add_ellipsoid(
            (side * 0.086, -0.016, 0.144),
            (0.018, 0.032, 0.048),
            PAL["hair_dark"],
            segs=8,
            rings=5,
            rot_euler=(0.10, 0.0, side * 0.12),
        )

    return pb.finalize_object(mat, world_pos=(0.0, 0.0, 1.535))


def build_upper_arm(mat, side_label, side_sign):
    """Omuz pivotundan (0,0,0) asagi -0.295m dirsege uzanan gercek insan ust kolu."""
    pb = PartBuilder(f"Human_UpperArm_{side_label}")
    # Omuz (deltoid) tunik yen kismi
    pb.add_ellipsoid(
        (side_sign * 0.008, 0.0, -0.010), (0.062, 0.058, 0.062), PAL["tunic"], segs=12, rings=7
    )
    pb.add_loft(
        [
            (-0.205, 0.046, 0.048, 0.0, 0.0),
            (-0.110, 0.054, 0.054, 0.0, 0.0),
            (-0.020, 0.058, 0.056, side_sign * 0.004, 0.0),
        ],
        PAL["tunic"],
    )
    # Katlanmis kol manseti
    pb.add_loft(
        [
            (-0.222, 0.050, 0.052, 0.0, 0.0),
            (-0.196, 0.050, 0.052, 0.0, 0.0),
        ],
        PAL["tunic_trim"],
    )
    # Dirsek ustu acik insan teni (biceps alt / dirsek)
    pb.add_loft(
        [
            (-0.295, 0.041, 0.042, 0.0, 0.0),
            (-0.220, 0.044, 0.045, 0.0, 0.0),
        ],
        PAL["skin"],
    )
    pb.add_ellipsoid((0.0, 0.0, -0.295), (0.041, 0.041, 0.038), PAL["skin_shade"], segs=10, rings=6)
    return pb.finalize_object(mat, world_pos=(side_sign * 0.215, 0.0, 1.395))


def build_forearm(mat, side_label, side_sign):
    """Dirsek pivotundan (0,0,0) asagi -0.265m bilege uzanan anatomik insan onkolu + deri kolcak."""
    pb = PartBuilder(f"Human_Forearm_{side_label}")
    # Dirsekten orta onkola gercek insan teni (kas konturu)
    pb.add_loft(
        [
            (-0.115, 0.039, 0.040, 0.0, 0.0),
            (-0.055, 0.045, 0.044, 0.0, 0.0),
            (0.005, 0.041, 0.041, 0.0, 0.0),
        ],
        PAL["skin"],
    )
    # Deri bileklik / maceraci kolcak (bracer) + pirinc percin
    pb.add_loft(
        [
            (-0.242, 0.034, 0.031, 0.0, 0.0),
            (-0.180, 0.038, 0.036, 0.0, 0.0),
            (-0.110, 0.042, 0.042, 0.0, 0.0),
        ],
        PAL["leather"],
    )
    pb.add_loft(
        [
            (-0.246, 0.036, 0.033, 0.0, 0.0),
            (-0.232, 0.036, 0.033, 0.0, 0.0),
        ],
        PAL["brass"],
    )
    # Bilek ten gecisi
    pb.add_loft(
        [
            (-0.268, 0.030, 0.026, 0.0, 0.0),
            (-0.240, 0.032, 0.028, 0.0, 0.0),
        ],
        PAL["skin"],
    )
    return pb.finalize_object(mat, world_pos=(side_sign * 0.215, 0.0, 1.100), smooth_shading=True)


def build_hand(mat, side_label, side_sign):
    """
    Bilekten (0,0,0) uzanan TEK PARCA puruzsuz insan eli + kavisli basparmak:
    - Avuc-one durusunda basparmak dis yanda (thumb_side = side_sign -> Sag +X, Sol -X).
    - Silindirik kabza tuneli X eksenindedir (by=-0.018, bz=-0.056 -> uy=-0.056, uz=+0.018).
    """
    pb = PartBuilder(f"Human_Hand_{side_label}")
    thumb_side = side_sign
    x_axis = (1.0, 0.0, 0.0)

    # 1. Tek Parca Bilek -> Avuc -> Bogum -> C-Kivrimli 4 Parmak Plakasi (by=-0.018, bz=-0.056)
    pb.add_curved_tube(
        [
            ((0.0, 0.000, 0.006), 0.026, 0.019, 0.0000, PAL["skin"], x_axis),
            ((0.0, 0.002, -0.014), 0.029, 0.017, 0.0000, PAL["skin"], x_axis),
            ((thumb_side * 0.003, 0.006, -0.036), 0.035, 0.014, 0.0006, PAL["skin"], x_axis),
            ((thumb_side * 0.002, 0.008, -0.058), 0.037, 0.012, 0.0018, PAL["skin"], x_axis),
            ((0.0, -0.008, -0.080), 0.035, 0.010, 0.0022, PAL["skin"], x_axis),
            ((0.0, -0.031, -0.078), 0.033, 0.0095, 0.0022, PAL["skin_shade"], x_axis),
            ((0.0, -0.041, -0.058), 0.031, 0.0088, 0.0018, PAL["skin_shade"], x_axis),
            ((0.0, -0.033, -0.044), 0.028, 0.0072, 0.0012, PAL["skin_shade"], x_axis),
        ],
        segs=18,
    )

    # 2. Tek Parca Kavisli Basparmak (thenar kas kokunden cikip kabza tunelini ustten kilitler)
    t_axis = (0.0, 0.0, 1.0)
    pb.add_curved_tube(
        [
            ((thumb_side * 0.024, 0.000, -0.024), 0.014, 0.015, 0.0, PAL["skin"], t_axis),
            ((thumb_side * 0.036, -0.010, -0.038), 0.012, 0.013, 0.0, PAL["skin"], t_axis),
            ((thumb_side * 0.037, -0.025, -0.050), 0.0105, 0.011, 0.0, PAL["skin"], t_axis),
            ((thumb_side * 0.026, -0.036, -0.056), 0.0095, 0.0095, 0.0, PAL["skin_shade"], t_axis),
            ((thumb_side * 0.013, -0.037, -0.055), 0.0080, 0.0080, 0.0, PAL["skin_shade"], t_axis),
        ],
        segs=12,
    )

    return pb.finalize_object(mat, world_pos=(side_sign * 0.215, 0.0, 0.835), smooth_shading=True)


def build_thigh(mat, side_label, side_sign):
    pb = PartBuilder(f"Human_Thigh_{side_label}")
    pb.add_loft(
        [
            (-0.430, 0.056, 0.058, 0.0, 0.002),
            (-0.280, 0.068, 0.072, 0.0, 0.000),
            (-0.120, 0.080, 0.082, 0.0, -0.002),
            (0.010, 0.082, 0.082, 0.0, 0.000),
        ],
        PAL["trousers"],
    )
    # Deri dizlik pedi
    pb.add_ellipsoid((0.0, -0.054, -0.422), (0.046, 0.024, 0.052), PAL["trousers_pad"], segs=10, rings=6)
    return pb.finalize_object(mat, world_pos=(side_sign * 0.105, 0.0, 0.895))


def build_shin(mat, side_label, side_sign):
    pb = PartBuilder(f"Human_Shin_{side_label}")
    # Ust baldir pantolon
    pb.add_loft(
        [
            (-0.125, 0.056, 0.058, 0.0, 0.004),
            (-0.040, 0.058, 0.060, 0.0, 0.002),
            (0.010, 0.054, 0.056, 0.0, 0.000),
        ],
        PAL["trousers"],
    )
    # Diz alti deri cizme koncu + katlanmis manset
    pb.add_loft(
        [
            (-0.145, 0.062, 0.064, 0.0, 0.004),
            (-0.115, 0.064, 0.066, 0.0, 0.004),
        ],
        PAL["leather"],
    )
    pb.add_loft(
        [
            (-0.410, 0.044, 0.048, 0.0, 0.002),
            (-0.260, 0.054, 0.058, 0.0, 0.008),
            (-0.135, 0.058, 0.060, 0.0, 0.004),
        ],
        PAL["boot"],
    )
    return pb.finalize_object(mat, world_pos=(side_sign * 0.105, 0.0, 0.465))


def build_foot(mat, side_label, side_sign):
    pb = PartBuilder(f"Human_Foot_{side_label}")
    # Deri bot govdesi (topuktan parmak ucuna -Y yonunde ileri)
    pb.add_box((0.0, -0.055, -0.040), (0.050, 0.118, 0.036), PAL["boot"], bevel_amt=0.010)
    pb.add_ellipsoid((0.0, -0.155, -0.048), (0.048, 0.044, 0.028), PAL["boot"], segs=10, rings=6)
    # Koyu bot tabani (sole)
    pb.add_box((0.0, -0.062, -0.078), (0.053, 0.130, 0.014), PAL["boot_sole"], bevel_amt=0.004)
    return pb.finalize_object(mat, world_pos=(side_sign * 0.105, 0.0, 0.085))


def export_articulated_glb(parts):
    """
    Her parcanin kendi pivot origin'ini koruyarak (world_pos=0 sifirlanarak) tek GLB'ye ihrac eder;
    Unity GlbHierarchyLoader her parca mesh'ini tam eklem pivotu uzerinde yukler!
    """
    os.makedirs(os.path.dirname(OUT_GLB), exist_ok=True)
    saved_locs = {obj.name: obj.location.copy() for obj in parts}
    for obj in parts:
        obj.location = Vector((0.0, 0.0, 0.0))

    bpy.ops.object.select_all(action="DESELECT")
    for obj in parts:
        obj.select_set(True)

    bpy.ops.export_scene.gltf(
        filepath=OUT_GLB,
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

    for obj in parts:
        obj.location = saved_locs[obj.name]


def render_4_angle_contact_sheet():
    """4 acili 512x512 render alir ve 1024x1024 contact sheet olarak kaydeder."""
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

    # Temiz acik gri studyo arka plan + 3 nokta isik
    world = bpy.data.worlds.new("PreviewWorld")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs["Color"].default_value = (0.16, 0.19, 0.24, 1.0)
        bg.inputs["Strength"].default_value = 0.9

    lights = [
        ("KeyLight", (2.0, -3.2, 3.4), 140.0),
        ("FillLight", (-2.6, -2.0, 1.8), 65.0),
        ("RimLight", (0.2, 3.0, 2.6), 90.0),
    ]
    for lname, lpos, power in lights:
        ldata = bpy.data.lights.new(name=lname, type="POINT")
        ldata.energy = power
        lobj = bpy.data.objects.new(name=lname, object_data=ldata)
        lobj.location = Vector(lpos)
        bpy.context.collection.objects.link(lobj)

    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_obj = bpy.data.objects.new("PreviewCam", cam_data)
    bpy.context.collection.objects.link(cam_obj)
    scene.camera = cam_obj

    views = [
        ("face", Vector((0.0, -0.78, 1.66)), Vector((0.0, 0.0, 1.64)), 60.0),
        ("front", Vector((0.0, -3.15, 0.96)), Vector((0.0, 0.0, 0.92)), 48.0),
        ("iso", Vector((-2.15, -2.35, 1.38)), Vector((0.0, 0.0, 0.92)), 48.0),
        ("side", Vector((-3.05, 0.0, 0.96)), Vector((0.0, 0.0, 0.92)), 48.0),
    ]

    temp_paths = []
    for tag, pos, target, lens in views:
        cam_obj.location = pos
        cam_data.lens = lens
        direction = (target - pos).normalized()
        cam_obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
        out_p = os.path.join(OUT_PREVIEW_DIR, f"_tmp_human_{tag}.png")
        scene.render.filepath = out_p
        bpy.ops.render.render(write_still=True)
        temp_paths.append(out_p)

    # 2x2 contact sheet birlestirme (saf Blender Image API — PIL gerektirmez)
    sheet = bpy.data.images.new("HumanContactSheet", width=1024, height=1024, alpha=True)
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
    sheet.filepath_raw = OUT_CONTACT_SHEET
    sheet.file_format = "PNG"
    sheet.save()

    # Ek Yakin Plan FPS El + Kabza Tuneli Denetim Render'i (GLB ihracindan SONRA calisir!)
    rod_pb = PartBuilder("_PreviewTestHiltRod")
    rod_pb.add_box(
        (-0.215, -0.018, 0.835 - 0.056),
        (0.110, 0.014, 0.014),
        PAL["leather_dark"],
        bevel_amt=0.004,
    )
    rod_pb.add_box(
        (-0.095, -0.018, 0.835 - 0.056),
        (0.010, 0.048, 0.018),
        PAL["brass"],
        bevel_amt=0.003,
    )
    rod_obj = rod_pb.finalize_object(bpy.data.materials[0], world_pos=(0.0, 0.0, 0.0))

    cam_obj.location = Vector((-0.06, -0.26, 0.86))
    cam_data.lens = 50.0
    hand_target = Vector((-0.215, -0.018, 0.785))
    cam_obj.rotation_euler = (
        (hand_target - cam_obj.location).normalized().to_track_quat("-Z", "Y").to_euler()
    )
    closeup_path = os.path.join(OUT_PREVIEW_DIR, "player_hand_grip_closeup.png")
    scene.render.filepath = closeup_path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(rod_obj, do_unlink=True)


def main():
    clear_scene()
    mat = create_vertex_color_material()

    # Blender native front = -Y, Right = -X, Left = +X, Up = +Z
    parts = [
        build_pelvis(mat),
        build_torso(mat),
        build_neck(mat),
        build_head(mat),
        build_upper_arm(mat, "R", -1.0),
        build_forearm(mat, "R", -1.0),
        build_hand(mat, "R", -1.0),
        build_upper_arm(mat, "L", 1.0),
        build_forearm(mat, "L", 1.0),
        build_hand(mat, "L", 1.0),
        build_thigh(mat, "L", 1.0),
        build_shin(mat, "L", 1.0),
        build_foot(mat, "L", 1.0),
        build_thigh(mat, "R", -1.0),
        build_shin(mat, "R", -1.0),
        build_foot(mat, "R", -1.0),
    ]

    total_tris = sum(len(o.data.polygons) for o in parts)
    export_articulated_glb(parts)
    render_4_angle_contact_sheet()
    print(f"[HumanCharacterBuilder] OK: {len(parts)} eklem parcasi, {total_tris} ucgen -> {OUT_GLB}")
    print(f"[HumanCharacterBuilder] Preview: {OUT_CONTACT_SHEET}")


if __name__ == "__main__":
    main()
