"""
Stratocraft Otonom 3B Fabrika — 6 Gercekci Eklemli Yaratik Modeli Ureticisi (Blender 4.2).

Kullanim (PATH uzerindeki "blender" veya dogrudan Python sarmalayicisi ile, SIFIR sabit yol):
    blender --background --python Tools/AutonomousAssetFactory/generators/creature_anatomical_builder.py
    veya:
    python Tools/AutonomousAssetFactory/generators/creature_anatomical_builder.py

Ciktilar (0 gomulu doku, UV-Free COLOR_0 tepe renkleri):
    - Assets/Art/Generated/Creatures_21/creature_dire_wolf.glb
    - Assets/Art/Generated/Creatures_21/creature_horned_behemoth.glb
    - Assets/Art/Generated/Creatures_21/creature_stag_steed.glb
    - Assets/Art/Generated/Creatures_21/creature_arachnid_stalker.glb (gercek 8 bacakli!)
    - Assets/Art/Generated/Creatures_21/creature_griffin_raptor.glb
    - Assets/Art/Generated/Creatures_21/creature_ancient_golem.glb
    - BuildLogs/Previews/creature_*_contact_sheet.png (6 adet 4 acili 512x512 denetim karti)
"""

import math
import os
import shutil
import subprocess
import sys

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, "..", "..", ".."))
OUT_CREATURES_DIR = os.path.join(REPO_ROOT, "Assets", "Art", "Generated", "Creatures_21")
OUT_PREVIEW_DIR = os.path.join(REPO_ROOT, "BuildLogs", "Previews")


def _run_via_blender_cli_if_needed():
    try:
        import bpy  # noqa: F401

        return False
    except ImportError:
        blender_bin = shutil.which("blender")
        if not blender_bin:
            rel_blender = os.path.join(REPO_ROOT, "Tools", "bin", "blender-4.2", "blender.exe")
            blender_bin = rel_blender if os.path.exists(rel_blender) else "blender"
        cmd = [blender_bin, "--background", "--python", os.path.abspath(__file__)]
        subprocess.run(cmd, check=True, cwd=REPO_ROOT)
        return True


if _run_via_blender_cli_if_needed():
    sys.exit(0)

import bmesh  # noqa: E402
import bpy  # noqa: E402
from mathutils import Euler, Matrix, Vector  # noqa: E402

# Dogal yaratik renk paleti (sRGB 0..255 -> glTF COLOR_0; R >= 18)
PAL = {
    # Dire Wolf (dogal bozkurt gumus-boz kurk + koyu sirt + krem gogus)
    "wolf_coat": (154, 150, 142, 255),
    "wolf_dorsal": (84, 86, 94, 255),
    "wolf_mane": (122, 118, 112, 255),
    "wolf_belly": (222, 218, 208, 255),
    "wolf_ear_in": (198, 166, 154, 255),
    "wolf_eye": (248, 188, 44, 255),
    # Horned Behemoth (zengin gergedan/bizon kahve-arduvaz post + zirh plakalari)
    "beh_hide": (134, 114, 100, 255),
    "beh_plate": (86, 80, 84, 255),
    "beh_belly": (166, 144, 124, 255),
    "beh_horn": (228, 214, 184, 255),
    "beh_horn_tip": (108, 92, 76, 255),
    "beh_eye": (250, 128, 38, 255),
    # Stag Steed (sicak kizil-kestane asil geyik/at kurku)
    "stag_coat": (182, 118, 72, 255),
    "stag_dark": (104, 66, 44, 255),
    "stag_belly": (226, 206, 178, 255),
    "stag_antler": (234, 218, 186, 255),
    "stag_hoof": (46, 40, 38, 255),
    "stag_tack": (92, 58, 38, 255),
    "stag_brass": (224, 182, 80, 255),
    # Arachnid Stalker (8-legged)
    "spid_chitin": (52, 54, 66, 255),
    "spid_carapace": (86, 78, 98, 255),
    "spid_band": (186, 126, 52, 255),
    "spid_mark": (212, 62, 46, 255),
    "spid_eye": (244, 42, 52, 255),
    "spid_fang": (32, 30, 38, 255),
    # Griffin Raptor
    "grif_eagle": (152, 98, 52, 255),
    "grif_plume": (196, 142, 78, 255),
    "grif_crest": (242, 234, 214, 255),
    "grif_lion": (208, 158, 88, 255),
    "grif_lion_shade": (168, 120, 62, 255),
    "grif_beak": (242, 188, 52, 255),
    "grif_talon": (38, 36, 42, 255),
    # Ancient Golem
    "gol_stone": (140, 144, 150, 255),
    "gol_stone_dark": (92, 96, 104, 255),
    "gol_stone_light": (176, 180, 184, 255),
    "gol_moss": (92, 148, 74, 255),
    "gol_bronze": (184, 142, 76, 255),
    "gol_rune": (80, 228, 246, 255),
    # Shared details
    "ivory_fang": (242, 236, 220, 255),
    "mouth_dark": (96, 44, 46, 255),
    "dark_claw": (34, 32, 38, 255),
    "pupil": (22, 24, 28, 255),
    "sclera": (242, 244, 246, 255),
}


def srgb_to_linear_rgba(rgba_u8):
    """Blender 4.2 BYTE_COLOR attribute -> glTF COLOR_0 icin linear float4 donusumu."""

    def _c(v):
        x = max(0.0, min(1.0, v / 255.0))
        return x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4

    return (_c(rgba_u8[0]), _c(rgba_u8[1]), _c(rgba_u8[2]), rgba_u8[3] / 255.0)


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def create_vertex_color_material(name="M_CreatureVertexColor", roughness=0.66):
    mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    vcol = nt.nodes.new("ShaderNodeVertexColor")
    vcol.layer_name = "Col"
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Specular IOR Level"].default_value = 0.30
    nt.links.new(vcol.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


class PartBuilder:
    """Tek bir eklem parcasinin bmesh insa yardimcisi (yerel pivot = (0,0,0), -Y = on, +Z = yukari)."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.color_layer = self.bm.loops.layers.color.new("Col")

    def _paint_faces(self, faces, rgba_u8, shade_grad=0.08):
        lin = srgb_to_linear_rgba(rgba_u8)
        for f in faces:
            if not f.is_valid:
                continue
            n_up = f.normal.z if f.normal.length > 1e-5 else 0.0
            ao_factor = max(0.76, min(1.14, 0.96 + n_up * shade_grad))
            col = (
                min(1.0, lin[0] * ao_factor),
                min(1.0, lin[1] * ao_factor),
                min(1.0, lin[2] * ao_factor),
                lin[3],
            )
            for loop in f.loops:
                loop[self.color_layer] = col

    def add_curved_tube(
        self,
        stations,
        segs=14,
        cap_start=True,
        cap_end=True,
        power_x=0.92,
        power_n=0.92,
    ):
        """
        3B omurga/kas/boynuz/uzuv egrisi uzerindeki istasyonlardan:
        stations: [(center_xyz, rx, ry, rgba_u8[, axis_x_hint]), ...]
        su gecirmez organik loft tup uretir.
        """
        ring_verts = []
        ring_colors = []
        count = len(stations)
        prev_axis_x = None

        for i, st in enumerate(stations):
            center = Vector(st[0])
            rx = max(0.002, float(st[1]))
            ry = max(0.002, float(st[2]))
            rgba = st[3]
            ax_hint = Vector(st[4]) if len(st) > 4 else (prev_axis_x or Vector((1.0, 0.0, 0.0)))

            if i == 0:
                tangent = Vector(stations[1][0]) - center
            elif i == count - 1:
                tangent = center - Vector(stations[i - 1][0])
            else:
                tangent = Vector(stations[i + 1][0]) - Vector(stations[i - 1][0])
            if tangent.length < 1e-6:
                tangent = Vector((0.0, 0.0, 1.0))
            tangent.normalize()

            axis_x = ax_hint - tangent * ax_hint.dot(tangent)
            if axis_x.length < 1e-5:
                fallback = Vector((1.0, 0.0, 0.0)) if abs(tangent.x) < 0.9 else Vector((0.0, 1.0, 0.0))
                axis_x = fallback - tangent * fallback.dot(tangent)
            axis_x.normalize()
            if prev_axis_x is not None and axis_x.dot(prev_axis_x) < 0.0:
                axis_x = -axis_x
            prev_axis_x = axis_x
            axis_n = tangent.cross(axis_x).normalized()

            rv = []
            for s in range(segs):
                a = (2.0 * math.pi * s) / segs
                ca = math.cos(a)
                sa = math.sin(a)
                sx = math.copysign(abs(ca) ** power_x, ca) * rx
                sn = math.copysign(abs(sa) ** power_n, sa) * ry
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

    def add_ellipsoid(self, center, radii, rgba_u8, segs=12, rings=8, rot_euler=(0, 0, 0)):
        rot_m = Euler(rot_euler, "XYZ").to_matrix().to_4x4()
        scale_m = Matrix.Diagonal((radii[0], radii[1], radii[2], 1.0))
        xform = Matrix.Translation(Vector(center)) @ rot_m @ scale_m
        ret = bmesh.ops.create_uvsphere(self.bm, u_segments=segs, v_segments=rings, radius=1.0, matrix=xform)
        verts = set(ret["verts"])
        faces = [f for f in self.bm.faces if all(v in verts for v in f.verts)]
        bmesh.ops.recalc_face_normals(self.bm, faces=faces)
        self._paint_faces(faces, rgba_u8)
        return faces

    def add_beveled_plate(self, center, half_extents, rgba_u8, rot_euler=(0, 0, 0), bevel_amt=0.012):
        prev_faces = set(self.bm.faces)
        rot_m = Euler(rot_euler, "XYZ").to_matrix().to_4x4()
        scale_m = Matrix.Diagonal((half_extents[0] * 2.0, half_extents[1] * 2.0, half_extents[2] * 2.0, 1.0))
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


def create_gore_sockets(spec_list):
    """SKILL.md Bolum 2 uzuv kopma soket dugumlerini olusturur."""
    empties = []
    for sname, spos in spec_list:
        emp = bpy.data.objects.new(sname, None)
        emp.empty_display_size = 0.08
        emp.location = Vector(spos)
        bpy.context.collection.objects.link(emp)
        empties.append(emp)
    return empties


# ==============================================================================
# 1. CREATURE_DIRE_WOLF (Ulu Kurt — Yirtici Eklemli Dort-Ayakli)
# ==============================================================================
def build_dire_wolf(mat):
    parts = []

    # Torso (world pivot = 0, 0, 0.68)
    pb = PartBuilder("Wolf_Torso")
    pb.add_curved_tube(
        [
            ((0.0, 0.44, 0.02), 0.13, 0.14, PAL["wolf_coat"]),
            ((0.0, 0.28, 0.04), 0.17, 0.18, PAL["wolf_coat"]),
            ((0.0, 0.08, 0.03), 0.16, 0.17, PAL["wolf_coat"]),
            ((0.0, -0.14, 0.04), 0.21, 0.24, PAL["wolf_mane"]),
            ((0.0, -0.32, 0.08), 0.19, 0.22, PAL["wolf_mane"]),
            ((0.0, -0.43, 0.13), 0.13, 0.15, PAL["wolf_dorsal"]),
        ],
        segs=16,
    )
    # Acik krem karin ve gogus kurku + koyu sirt yelesi
    pb.add_ellipsoid((0.0, -0.18, -0.07), (0.16, 0.26, 0.14), PAL["wolf_belly"], segs=12, rings=8)
    for my, mz, rx, rz in [
        (-0.34, 0.21, 0.15, 0.09),
        (-0.20, 0.23, 0.14, 0.08),
        (-0.04, 0.19, 0.10, 0.06),
        (0.14, 0.19, 0.09, 0.05),
    ]:
        pb.add_ellipsoid(
            (0.0, my, mz), (rx, 0.12, rz), PAL["wolf_dorsal"], segs=10, rings=6, rot_euler=(-0.22, 0, 0)
        )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, 0.0, 0.68)))

    # Head (world pivot = 0, -0.40, 0.80)
    pb = PartBuilder("Wolf_Head")
    pb.add_curved_tube(
        [
            ((0.0, 0.04, -0.02), 0.13, 0.13, PAL["wolf_mane"]),
            ((0.0, -0.05, 0.02), 0.14, 0.13, PAL["wolf_coat"]),
            ((0.0, -0.14, 0.01), 0.10, 0.095, PAL["wolf_coat"]),
            ((0.0, -0.24, -0.015), 0.068, 0.062, PAL["wolf_dorsal"]),
            ((0.0, -0.31, -0.020), 0.046, 0.044, PAL["wolf_dorsal"]),
        ],
        segs=14,
    )
    # Yanak kurk tutamlari (kafatasina yumusak gecisli)
    for side in (-1.0, 1.0):
        pb.add_ellipsoid(
            (side * 0.112, -0.04, -0.025),
            (0.046, 0.082, 0.058),
            PAL["wolf_belly"],
            segs=10,
            rings=6,
            rot_euler=(0.14, -side * 0.18, side * 0.28),
        )
        # Dik ucgenimsi kurt kulaklari + ic kulak
        pb.add_curved_tube(
            [
                ((side * 0.086, 0.01, 0.095), 0.042, 0.028, PAL["wolf_dorsal"]),
                ((side * 0.104, 0.02, 0.180), 0.030, 0.020, PAL["wolf_coat"]),
                ((side * 0.110, 0.03, 0.255), 0.010, 0.008, PAL["wolf_dorsal"]),
            ],
            segs=10,
        )
        pb.add_ellipsoid(
            (side * 0.096, -0.004, 0.165), (0.018, 0.010, 0.042), PAL["wolf_ear_in"], segs=8, rings=5
        )
        # Kehribar yirtici kurt gozleri + kas kemeri (dis yuzeyde net gorunur)
        pb.add_ellipsoid(
            (side * 0.092, -0.122, 0.048), (0.024, 0.026, 0.020), PAL["wolf_eye"], segs=10, rings=6
        )
        pb.add_ellipsoid((side * 0.108, -0.134, 0.048), (0.010, 0.012, 0.012), PAL["pupil"], segs=8, rings=5)
        pb.add_beveled_plate(
            (side * 0.086, -0.120, 0.070),
            (0.028, 0.030, 0.010),
            PAL["wolf_dorsal"],
            rot_euler=(0.18, -side * 0.28, side * 0.15),
            bevel_amt=0.004,
        )
        # Ust kopek disleri (fangs)
        pb.add_curved_tube(
            [
                ((side * 0.040, -0.255, -0.045), 0.011, 0.011, PAL["ivory_fang"]),
                ((side * 0.038, -0.260, -0.082), 0.003, 0.003, PAL["ivory_fang"]),
            ],
            segs=8,
        )
    # Islak siyah kurt burnu
    pb.add_ellipsoid((0.0, -0.322, -0.012), (0.032, 0.024, 0.024), PAL["pupil"], segs=10, rings=6)
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.40, 0.80)))

    # Jaw (world pivot = 0, -0.48, 0.74)
    pb = PartBuilder("Wolf_Jaw")
    pb.add_curved_tube(
        [
            ((0.0, 0.02, 0.00), 0.068, 0.036, PAL["wolf_belly"]),
            ((0.0, -0.09, -0.012), 0.052, 0.028, PAL["wolf_belly"]),
            ((0.0, -0.20, -0.018), 0.036, 0.022, PAL["wolf_dorsal"]),
        ],
        segs=12,
    )
    pb.add_ellipsoid((0.0, -0.10, 0.008), (0.040, 0.085, 0.012), PAL["mouth_dark"], segs=8, rings=5)
    for side in (-1.0, 1.0):
        pb.add_curved_tube(
            [
                ((side * 0.030, -0.175, -0.005), 0.009, 0.009, PAL["ivory_fang"]),
                ((side * 0.030, -0.178, 0.024), 0.003, 0.003, PAL["ivory_fang"]),
            ],
            segs=8,
        )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.48, 0.74)))

    # Tail (world pivot = 0, 0.42, 0.72)
    pb = PartBuilder("Wolf_Tail")
    pb.add_curved_tube(
        [
            ((0.0, -0.02, 0.00), 0.058, 0.058, PAL["wolf_dorsal"]),
            ((0.0, 0.14, -0.05), 0.076, 0.072, PAL["wolf_coat"]),
            ((0.0, 0.32, -0.14), 0.082, 0.076, PAL["wolf_coat"]),
            ((0.0, 0.48, -0.22), 0.060, 0.056, PAL["wolf_dorsal"]),
            ((0.0, 0.58, -0.24), 0.024, 0.024, PAL["wolf_belly"]),
        ],
        segs=12,
    )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, 0.42, 0.72)))

    # Front & Hind Legs (digitigrade paws resting on world z = 0.0)
    for side_lbl, sgn in (("L", 1.0), ("R", -1.0)):
        # Front Upper (world pivot = sgn*0.17, -0.26, 0.62 -> elbow rel (0, +0.03, -0.30))
        pb = PartBuilder(f"Wolf_FrontUpper_{side_lbl}")
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.04), 0.078, 0.092, PAL["wolf_mane"]),
                ((0.0, 0.01, -0.12), 0.064, 0.074, PAL["wolf_coat"]),
                ((0.0, 0.03, -0.30), 0.048, 0.052, PAL["wolf_coat"]),
            ],
            segs=12,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.17, -0.26, 0.62)))

        # Front Lower (world pivot = sgn*0.17, -0.23, 0.32 -> ground z=-0.32)
        pb = PartBuilder(f"Wolf_FrontLower_{side_lbl}")
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.02), 0.048, 0.050, PAL["wolf_coat"]),
                ((0.0, -0.01, -0.16), 0.038, 0.040, PAL["wolf_belly"]),
                ((0.0, -0.04, -0.27), 0.042, 0.046, PAL["wolf_belly"]),
                ((0.0, -0.08, -0.31), 0.052, 0.058, PAL["wolf_coat"]),
            ],
            segs=12,
        )
        for tx in (-0.026, 0.0, 0.026):
            pb.add_ellipsoid((tx, -0.125, -0.302), (0.016, 0.028, 0.018), PAL["wolf_belly"], segs=8, rings=5)
            pb.add_ellipsoid((tx, -0.152, -0.308), (0.007, 0.016, 0.009), PAL["dark_claw"], segs=6, rings=4)
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.17, -0.23, 0.32)))

        # Hind Upper (world pivot = sgn*0.18, 0.30, 0.64 -> hock rel (0, +0.06, -0.32))
        pb = PartBuilder(f"Wolf_HindUpper_{side_lbl}")
        pb.add_curved_tube(
            [
                ((0.0, -0.01, 0.05), 0.086, 0.110, PAL["wolf_coat"]),
                ((0.0, 0.02, -0.14), 0.072, 0.090, PAL["wolf_coat"]),
                ((0.0, 0.06, -0.32), 0.048, 0.056, PAL["wolf_dorsal"]),
            ],
            segs=12,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.18, 0.30, 0.64)))

        # Hind Lower (world pivot = sgn*0.18, 0.36, 0.32 -> ground z=-0.32)
        pb = PartBuilder(f"Wolf_HindLower_{side_lbl}")
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.02), 0.048, 0.054, PAL["wolf_dorsal"]),
                ((0.0, -0.04, -0.16), 0.036, 0.040, PAL["wolf_belly"]),
                ((0.0, -0.07, -0.28), 0.044, 0.048, PAL["wolf_belly"]),
                ((0.0, -0.11, -0.31), 0.052, 0.058, PAL["wolf_coat"]),
            ],
            segs=12,
        )
        for tx in (-0.026, 0.0, 0.026):
            pb.add_ellipsoid((tx, -0.150, -0.302), (0.016, 0.028, 0.018), PAL["wolf_belly"], segs=8, rings=5)
            pb.add_ellipsoid((tx, -0.176, -0.308), (0.007, 0.016, 0.009), PAL["dark_claw"], segs=6, rings=4)
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.18, 0.36, 0.32)))

    sockets = create_gore_sockets(
        [
            ("Socket_GoreCap_Head", (0.0, -0.40, 0.80)),
            ("Socket_GoreCap_ArmL", (0.17, -0.26, 0.62)),
            ("Socket_GoreCap_ArmR", (-0.17, -0.26, 0.62)),
            ("Socket_GoreCap_LegL", (0.18, 0.30, 0.64)),
            ("Socket_GoreCap_LegR", (-0.18, 0.30, 0.64)),
        ]
    )
    views = [
        ("front", Vector((0.0, -2.55, 0.72)), Vector((0.0, -0.05, 0.54)), 50.0),
        ("side", Vector((-2.65, 0.02, 0.68)), Vector((0.0, 0.02, 0.52)), 48.0),
        ("top", Vector((0.0, -0.05, 2.85)), Vector((0.0, 0.02, 0.50)), 48.0),
        ("iso", Vector((-1.95, -2.05, 1.28)), Vector((0.0, 0.0, 0.52)), 48.0),
    ]
    return parts, sockets, views


# ==============================================================================
# 2. CREATURE_HORNED_BEHEMOTH (Zirhli Boynuzlu Behemoth / Toslayici Dev)
# ==============================================================================
def build_horned_behemoth(mat):
    parts = []

    # Torso (world pivot = 0, 0, 0.78)
    pb = PartBuilder("Behemoth_Torso")
    pb.add_curved_tube(
        [
            ((0.0, 0.56, -0.02), 0.24, 0.23, PAL["beh_hide"]),
            ((0.0, 0.32, 0.04), 0.32, 0.30, PAL["beh_hide"]),
            ((0.0, 0.04, 0.08), 0.35, 0.34, PAL["beh_hide"]),
            ((0.0, -0.24, 0.14), 0.38, 0.38, PAL["beh_plate"]),
            ((0.0, -0.46, 0.10), 0.30, 0.31, PAL["beh_plate"]),
            ((0.0, -0.58, 0.04), 0.20, 0.21, PAL["beh_hide"]),
        ],
        segs=16,
    )
    pb.add_ellipsoid((0.0, -0.08, -0.10), (0.30, 0.44, 0.20), PAL["beh_belly"], segs=12, rings=8)
    # Sirt boyunca kavisli zirhli osteoderm sirt plakalari ve dikenleri
    for py, pz, sx, sy in [
        (-0.28, 0.43, 0.21, 0.15),
        (-0.08, 0.39, 0.22, 0.15),
        (0.12, 0.33, 0.19, 0.14),
        (0.32, 0.25, 0.15, 0.12),
    ]:
        pb.add_ellipsoid(
            (0.0, py, pz), (sx, sy, 0.065), PAL["beh_plate"], segs=12, rings=6, rot_euler=(-0.16, 0, 0)
        )
        pb.add_curved_tube(
            [
                ((0.0, py, pz + 0.03), 0.045, 0.065, PAL["beh_horn_tip"]),
                ((0.0, py + 0.05, pz + 0.15), 0.012, 0.016, PAL["beh_horn"]),
            ],
            segs=8,
        )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, 0.0, 0.78)))

    # Head (world pivot = 0, -0.52, 0.82)
    pb = PartBuilder("Behemoth_Head")
    pb.add_curved_tube(
        [
            ((0.0, 0.06, 0.02), 0.21, 0.20, PAL["beh_plate"]),
            ((0.0, -0.10, 0.00), 0.22, 0.19, PAL["beh_plate"]),
            ((0.0, -0.26, -0.04), 0.17, 0.15, PAL["beh_hide"]),
            ((0.0, -0.38, -0.07), 0.13, 0.11, PAL["beh_hide"]),
        ],
        segs=14,
    )
    # Zirhli alin kalkani + gergedan tipi burun boynuzu
    pb.add_beveled_plate(
        (0.0, -0.14, 0.14), (0.18, 0.15, 0.042), PAL["beh_plate"], rot_euler=(0.32, 0, 0), bevel_amt=0.014
    )
    pb.add_curved_tube(
        [
            ((0.0, -0.32, 0.00), 0.062, 0.072, PAL["beh_horn_tip"]),
            ((0.0, -0.40, 0.12), 0.040, 0.046, PAL["beh_horn"]),
            ((0.0, -0.43, 0.26), 0.010, 0.012, PAL["beh_horn"]),
        ],
        segs=10,
    )
    # Iki yan dev kavisli savas boynuzlari + parlayan kor gozler
    for side in (-1.0, 1.0):
        pb.add_curved_tube(
            [
                ((side * 0.16, -0.08, 0.10), 0.068, 0.068, PAL["beh_horn_tip"]),
                ((side * 0.30, -0.16, 0.14), 0.054, 0.054, PAL["beh_horn"]),
                ((side * 0.40, -0.30, 0.22), 0.038, 0.038, PAL["beh_horn"]),
                ((side * 0.36, -0.44, 0.34), 0.012, 0.012, PAL["ivory_fang"]),
            ],
            segs=12,
        )
        pb.add_ellipsoid(
            (side * 0.195, -0.175, 0.025), (0.026, 0.030, 0.022), PAL["beh_eye"], segs=10, rings=6
        )
        pb.add_ellipsoid((side * 0.212, -0.185, 0.025), (0.012, 0.014, 0.013), PAL["pupil"], segs=8, rings=5)
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.52, 0.82)))

    # Jaw + Tusks (world pivot = 0, -0.60, 0.72)
    pb = PartBuilder("Behemoth_Jaw")
    pb.add_curved_tube(
        [
            ((0.0, 0.04, 0.00), 0.14, 0.065, PAL["beh_belly"]),
            ((0.0, -0.14, -0.02), 0.12, 0.055, PAL["beh_hide"]),
            ((0.0, -0.26, -0.02), 0.095, 0.045, PAL["beh_plate"]),
        ],
        segs=12,
    )
    for side in (-1.0, 1.0):
        pb.add_curved_tube(
            [
                ((side * 0.095, -0.20, -0.01), 0.028, 0.028, PAL["beh_horn"]),
                ((side * 0.135, -0.29, 0.06), 0.020, 0.020, PAL["ivory_fang"]),
                ((side * 0.145, -0.34, 0.15), 0.007, 0.007, PAL["ivory_fang"]),
            ],
            segs=10,
        )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.60, 0.72)))

    # Tail with Ankylosaur Club (world pivot = 0, 0.56, 0.78)
    pb = PartBuilder("Behemoth_Tail")
    pb.add_curved_tube(
        [
            ((0.0, -0.02, 0.00), 0.095, 0.090, PAL["beh_hide"]),
            ((0.0, 0.20, -0.08), 0.075, 0.072, PAL["beh_plate"]),
            ((0.0, 0.42, -0.14), 0.058, 0.056, PAL["beh_plate"]),
        ],
        segs=12,
    )
    pb.add_ellipsoid((0.0, 0.48, -0.15), (0.12, 0.14, 0.085), PAL["beh_plate"], segs=12, rings=7)
    for side in (-1.0, 1.0):
        pb.add_ellipsoid((side * 0.12, 0.48, -0.15), (0.055, 0.040, 0.035), PAL["beh_horn"], segs=8, rings=5)
    parts.append(pb.finalize_object(mat, world_pos=(0.0, 0.56, 0.78)))

    # Pillar Legs (resting on world z = 0.0)
    for side_lbl, sgn in (("L", 1.0), ("R", -1.0)):
        # Front Upper (world pivot = sgn*0.28, -0.32, 0.66 -> knee rel (0, 0, -0.34))
        pb = PartBuilder(f"Behemoth_FrontUpper_{side_lbl}")
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.06), 0.145, 0.155, PAL["beh_plate"]),
                ((0.0, 0.00, -0.16), 0.125, 0.132, PAL["beh_hide"]),
                ((0.0, 0.00, -0.34), 0.105, 0.110, PAL["beh_hide"]),
            ],
            segs=14,
        )
        pb.add_beveled_plate(
            (sgn * 0.06, -0.02, -0.06), (0.09, 0.13, 0.13), PAL["beh_plate"], bevel_amt=0.015
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.28, -0.32, 0.66)))

        # Front Lower (world pivot = sgn*0.28, -0.32, 0.32 -> ground z=-0.32)
        pb = PartBuilder(f"Behemoth_FrontLower_{side_lbl}")
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.02), 0.105, 0.110, PAL["beh_hide"]),
                ((0.0, -0.01, -0.18), 0.098, 0.102, PAL["beh_plate"]),
                ((0.0, -0.03, -0.31), 0.122, 0.130, PAL["beh_plate"]),
            ],
            segs=14,
        )
        for tx in (-0.065, 0.0, 0.065):
            pb.add_ellipsoid(
                (tx, -0.125, -0.292), (0.034, 0.052, 0.030), PAL["beh_horn_tip"], segs=8, rings=5
            )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.28, -0.32, 0.32)))

        # Hind Upper (world pivot = sgn*0.27, 0.36, 0.68 -> knee rel (0, 0, -0.36))
        pb = PartBuilder(f"Behemoth_HindUpper_{side_lbl}")
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.06), 0.150, 0.168, PAL["beh_hide"]),
                ((0.0, 0.01, -0.18), 0.125, 0.138, PAL["beh_hide"]),
                ((0.0, 0.00, -0.36), 0.102, 0.110, PAL["beh_plate"]),
            ],
            segs=14,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.27, 0.36, 0.68)))

        # Hind Lower (world pivot = sgn*0.27, 0.36, 0.32 -> ground z=-0.32)
        pb = PartBuilder(f"Behemoth_HindLower_{side_lbl}")
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.02), 0.102, 0.108, PAL["beh_plate"]),
                ((0.0, -0.01, -0.18), 0.095, 0.100, PAL["beh_plate"]),
                ((0.0, -0.03, -0.31), 0.118, 0.126, PAL["beh_plate"]),
            ],
            segs=14,
        )
        for tx in (-0.062, 0.0, 0.062):
            pb.add_ellipsoid(
                (tx, -0.120, -0.292), (0.032, 0.050, 0.030), PAL["beh_horn_tip"], segs=8, rings=5
            )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.27, 0.36, 0.32)))

    sockets = create_gore_sockets(
        [
            ("Socket_GoreCap_Head", (0.0, -0.52, 0.82)),
            ("Socket_GoreCap_ArmL", (0.28, -0.32, 0.66)),
            ("Socket_GoreCap_ArmR", (-0.28, -0.32, 0.66)),
            ("Socket_GoreCap_LegL", (0.27, 0.36, 0.68)),
            ("Socket_GoreCap_LegR", (-0.27, 0.36, 0.68)),
        ]
    )
    views = [
        ("front", Vector((0.0, -3.20, 0.92)), Vector((0.0, -0.08, 0.68)), 48.0),
        ("side", Vector((-3.30, 0.00, 0.86)), Vector((0.0, 0.00, 0.66)), 48.0),
        ("top", Vector((0.0, -0.05, 3.60)), Vector((0.0, 0.00, 0.65)), 48.0),
        ("iso", Vector((-2.40, -2.50, 1.55)), Vector((0.0, -0.04, 0.66)), 48.0),
    ]
    return parts, sockets, views


# ==============================================================================
# 3. CREATURE_STAG_STEED (Asil Boynuzlu Geyik-At Binek / Savas Ati)
# ==============================================================================
def build_stag_steed(mat):
    parts = []

    # Torso + Arched Neck (world pivot = 0, 0, 0.88)
    pb = PartBuilder("Stag_Torso")
    pb.add_curved_tube(
        [
            ((0.0, 0.48, 0.04), 0.17, 0.19, PAL["stag_coat"]),
            ((0.0, 0.26, 0.06), 0.20, 0.22, PAL["stag_coat"]),
            ((0.0, 0.00, 0.04), 0.20, 0.23, PAL["stag_coat"]),
            ((0.0, -0.26, 0.06), 0.22, 0.26, PAL["stag_coat"]),
            ((0.0, -0.38, 0.15), 0.15, 0.19, PAL["stag_coat"]),
            ((0.0, -0.44, 0.28), 0.11, 0.14, PAL["stag_coat"]),
            ((0.0, -0.46, 0.38), 0.095, 0.115, PAL["stag_dark"]),
        ],
        segs=16,
    )
    pb.add_ellipsoid((0.0, -0.06, -0.09), (0.16, 0.34, 0.14), PAL["stag_belly"], segs=12, rings=8)
    # Koyu yele + hafif binek eyer ortusu & pirinc toka
    pb.add_curved_tube(
        [
            ((0.0, -0.24, 0.26), 0.028, 0.055, PAL["stag_dark"]),
            ((0.0, -0.36, 0.33), 0.026, 0.052, PAL["stag_dark"]),
            ((0.0, -0.43, 0.43), 0.022, 0.042, PAL["stag_dark"]),
        ],
        segs=10,
    )
    pb.add_beveled_plate((0.0, -0.02, 0.23), (0.205, 0.19, 0.035), PAL["stag_tack"], bevel_amt=0.010)
    pb.add_beveled_plate((0.0, -0.02, 0.252), (0.210, 0.03, 0.018), PAL["stag_brass"], bevel_amt=0.005)
    parts.append(pb.finalize_object(mat, world_pos=(0.0, 0.0, 0.88)))

    # Head + Crown Antlers (world pivot = 0, -0.46, 1.24)
    pb = PartBuilder("Stag_Head")
    pb.add_curved_tube(
        [
            ((0.0, 0.04, 0.00), 0.105, 0.115, PAL["stag_coat"]),
            ((0.0, -0.08, -0.01), 0.098, 0.105, PAL["stag_coat"]),
            ((0.0, -0.22, -0.06), 0.068, 0.074, PAL["stag_coat"]),
            ((0.0, -0.33, -0.10), 0.050, 0.054, PAL["stag_dark"]),
        ],
        segs=14,
    )
    pb.add_ellipsoid((0.0, -0.345, -0.095), (0.038, 0.025, 0.026), PAL["pupil"], segs=10, rings=6)
    for side in (-1.0, 1.0):
        # Gozler ve uzun zarif kulaklar
        pb.add_ellipsoid((side * 0.088, -0.08, 0.02), (0.018, 0.024, 0.020), PAL["pupil"], segs=10, rings=6)
        pb.add_ellipsoid((side * 0.094, -0.072, 0.026), (0.007, 0.008, 0.008), PAL["sclera"], segs=6, rings=4)
        pb.add_curved_tube(
            [
                ((side * 0.075, 0.02, 0.07), 0.028, 0.018, PAL["stag_coat"]),
                ((side * 0.135, 0.05, 0.14), 0.024, 0.014, PAL["stag_coat"]),
                ((side * 0.165, 0.07, 0.19), 0.008, 0.006, PAL["stag_dark"]),
            ],
            segs=10,
        )
        # Cok catalli tac geyik boynuzlari (ana dal + alin catali + tac catali)
        pb.add_curved_tube(
            [
                ((side * 0.052, -0.01, 0.09), 0.026, 0.026, PAL["stag_antler"]),
                ((side * 0.140, 0.03, 0.21), 0.022, 0.022, PAL["stag_antler"]),
                ((side * 0.220, 0.08, 0.35), 0.017, 0.017, PAL["stag_antler"]),
                ((side * 0.240, 0.04, 0.50), 0.012, 0.012, PAL["stag_antler"]),
                ((side * 0.195, -0.04, 0.60), 0.005, 0.005, PAL["ivory_fang"]),
            ],
            segs=10,
        )
        # Alin catali (brow tine)
        pb.add_curved_tube(
            [
                ((side * 0.115, 0.01, 0.18), 0.015, 0.015, PAL["stag_antler"]),
                ((side * 0.155, -0.10, 0.24), 0.011, 0.011, PAL["stag_antler"]),
                ((side * 0.145, -0.19, 0.30), 0.004, 0.004, PAL["ivory_fang"]),
            ],
            segs=8,
        )
        # Ust tac catali (royal tine)
        pb.add_curved_tube(
            [
                ((side * 0.215, 0.07, 0.34), 0.013, 0.013, PAL["stag_antler"]),
                ((side * 0.295, 0.11, 0.45), 0.009, 0.009, PAL["stag_antler"]),
                ((side * 0.310, 0.08, 0.55), 0.004, 0.004, PAL["ivory_fang"]),
            ],
            segs=8,
        )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.46, 1.24)))

    # Jaw (world pivot = 0, -0.52, 1.18)
    pb = PartBuilder("Stag_Jaw")
    pb.add_curved_tube(
        [
            ((0.0, 0.02, 0.00), 0.052, 0.028, PAL["stag_belly"]),
            ((0.0, -0.14, -0.04), 0.038, 0.022, PAL["stag_coat"]),
            ((0.0, -0.25, -0.06), 0.028, 0.018, PAL["stag_dark"]),
        ],
        segs=10,
    )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.52, 1.18)))

    # Flowing Tail (world pivot = 0, 0.48, 0.94)
    pb = PartBuilder("Stag_Tail")
    pb.add_curved_tube(
        [
            ((0.0, -0.02, 0.00), 0.048, 0.048, PAL["stag_dark"]),
            ((0.0, 0.12, -0.08), 0.062, 0.056, PAL["stag_dark"]),
            ((0.0, 0.22, -0.26), 0.058, 0.050, PAL["stag_dark"]),
            ((0.0, 0.26, -0.42), 0.024, 0.022, PAL["stag_belly"]),
        ],
        segs=12,
    )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, 0.48, 0.94)))

    # Long Slender Equine/Cervid Legs + Hooves on z = 0.0
    for side_lbl, sgn in (("L", 1.0), ("R", -1.0)):
        # Front Upper (world pivot = sgn*0.17, -0.32, 0.80 -> knee rel (0, 0, -0.40))
        pb = PartBuilder(f"Stag_FrontUpper_{side_lbl}")
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.05), 0.076, 0.095, PAL["stag_coat"]),
                ((0.0, 0.00, -0.18), 0.056, 0.068, PAL["stag_coat"]),
                ((0.0, 0.00, -0.40), 0.044, 0.048, PAL["stag_dark"]),
            ],
            segs=12,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.17, -0.32, 0.80)))

        # Front Lower (world pivot = sgn*0.17, -0.32, 0.40 -> hoof at z=-0.40)
        pb = PartBuilder(f"Stag_FrontLower_{side_lbl}")
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.02), 0.044, 0.046, PAL["stag_dark"]),
                ((0.0, 0.00, -0.22), 0.032, 0.035, PAL["stag_dark"]),
                ((0.0, -0.02, -0.34), 0.038, 0.042, PAL["stag_belly"]),
                ((0.0, -0.04, -0.395), 0.046, 0.054, PAL["stag_hoof"]),
            ],
            segs=12,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.17, -0.32, 0.40)))

        # Hind Upper (world pivot = sgn*0.18, 0.36, 0.82 -> hock rel (0, +0.05, -0.42))
        pb = PartBuilder(f"Stag_HindUpper_{side_lbl}")
        pb.add_curved_tube(
            [
                ((0.0, -0.01, 0.06), 0.088, 0.115, PAL["stag_coat"]),
                ((0.0, 0.02, -0.18), 0.068, 0.086, PAL["stag_coat"]),
                ((0.0, 0.05, -0.42), 0.044, 0.052, PAL["stag_dark"]),
            ],
            segs=12,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.18, 0.36, 0.82)))

        # Hind Lower (world pivot = sgn*0.18, 0.41, 0.40 -> hoof at z=-0.40)
        pb = PartBuilder(f"Stag_HindLower_{side_lbl}")
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.02), 0.044, 0.050, PAL["stag_dark"]),
                ((0.0, -0.03, -0.22), 0.032, 0.036, PAL["stag_dark"]),
                ((0.0, -0.05, -0.34), 0.038, 0.042, PAL["stag_belly"]),
                ((0.0, -0.07, -0.395), 0.046, 0.054, PAL["stag_hoof"]),
            ],
            segs=12,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.18, 0.41, 0.40)))

    sockets = create_gore_sockets(
        [
            ("Socket_GoreCap_Head", (0.0, -0.46, 1.24)),
            ("Socket_GoreCap_ArmL", (0.17, -0.32, 0.80)),
            ("Socket_GoreCap_ArmR", (-0.17, -0.32, 0.80)),
            ("Socket_GoreCap_LegL", (0.18, 0.36, 0.82)),
            ("Socket_GoreCap_LegR", (-0.18, 0.36, 0.82)),
        ]
    )
    views = [
        ("front", Vector((0.0, -3.25, 1.05)), Vector((0.0, -0.08, 0.90)), 48.0),
        ("side", Vector((-3.30, -0.02, 0.98)), Vector((0.0, -0.02, 0.86)), 48.0),
        ("top", Vector((0.0, -0.05, 3.60)), Vector((0.0, 0.00, 0.85)), 48.0),
        ("iso", Vector((-2.35, -2.45, 1.65)), Vector((0.0, -0.04, 0.88)), 48.0),
    ]
    return parts, sockets, views


# ==============================================================================
# 4. CREATURE_ARACHNID_STALKER (GERCEK 8 BACAKLI Zirhli Orumcek / Akrep Avcisi)
# ==============================================================================
def build_arachnid_stalker(mat):
    parts = []

    # Cephalothorax Torso (world pivot = 0, 0, 0.38)
    pb = PartBuilder("Arachnid_Torso")
    pb.add_curved_tube(
        [
            ((0.0, 0.14, 0.00), 0.10, 0.09, PAL["spid_chitin"]),
            ((0.0, 0.02, 0.03), 0.24, 0.16, PAL["spid_carapace"]),
            ((0.0, -0.14, 0.04), 0.26, 0.17, PAL["spid_carapace"]),
            ((0.0, -0.28, 0.02), 0.19, 0.13, PAL["spid_chitin"]),
            ((0.0, -0.35, 0.00), 0.12, 0.09, PAL["spid_chitin"]),
        ],
        segs=16,
    )
    pb.add_beveled_plate((0.0, -0.10, 0.16), (0.14, 0.18, 0.035), PAL["spid_band"], bevel_amt=0.010)
    parts.append(pb.finalize_object(mat, world_pos=(0.0, 0.0, 0.38)))

    # Anterior Eye-Cluster Head (world pivot = 0, -0.30, 0.40)
    pb = PartBuilder("Arachnid_Head")
    pb.add_ellipsoid((0.0, -0.04, 0.02), (0.14, 0.10, 0.09), PAL["spid_chitin"], segs=12, rings=8)
    # 8 Parlak Yakut Orumcek Gozu (2 buyuk orta + 6 yan goz)
    eye_specs = [
        (0.032, -0.125, 0.045, 0.022),
        (-0.032, -0.125, 0.045, 0.022),
        (0.072, -0.112, 0.048, 0.016),
        (-0.072, -0.112, 0.048, 0.016),
        (0.102, -0.090, 0.042, 0.013),
        (-0.102, -0.090, 0.042, 0.013),
        (0.055, -0.105, 0.074, 0.013),
        (-0.055, -0.105, 0.074, 0.013),
    ]
    for ex, ey, ez, er in eye_specs:
        pb.add_ellipsoid((ex, ey, ez), (er, er * 0.85, er), PAL["spid_eye"], segs=8, rings=6)
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.30, 0.40)))

    # Chelicerae Fangs & Pedipalp Pincers (world pivot = 0, -0.38, 0.34)
    pb = PartBuilder("Arachnid_Jaw")
    for side in (-1.0, 1.0):
        # Zehirli ic disler (chelicerae)
        pb.add_curved_tube(
            [
                ((side * 0.048, 0.00, 0.03), 0.036, 0.042, PAL["spid_carapace"]),
                ((side * 0.055, -0.07, -0.02), 0.024, 0.028, PAL["spid_fang"]),
                ((side * 0.025, -0.12, -0.09), 0.006, 0.006, PAL["spid_eye"]),
            ],
            segs=10,
        )
        # On dokunaç / kiskac (pedipalps)
        pb.add_curved_tube(
            [
                ((side * 0.115, 0.03, 0.02), 0.024, 0.024, PAL["spid_chitin"]),
                ((side * 0.175, -0.08, 0.04), 0.022, 0.022, PAL["spid_band"]),
                ((side * 0.145, -0.20, -0.03), 0.016, 0.022, PAL["spid_chitin"]),
                ((side * 0.105, -0.25, -0.07), 0.006, 0.009, PAL["spid_fang"]),
            ],
            segs=10,
        )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.38, 0.34)))

    # Segmented Bulbous Abdomen + Stinger Tip (world pivot = 0, 0.10, 0.42)
    pb = PartBuilder("Arachnid_Tail")
    pb.add_curved_tube(
        [
            ((0.0, -0.02, -0.02), 0.09, 0.08, PAL["spid_chitin"]),
            ((0.0, 0.12, 0.06), 0.24, 0.20, PAL["spid_carapace"]),
            ((0.0, 0.30, 0.11), 0.31, 0.26, PAL["spid_carapace"]),
            ((0.0, 0.48, 0.10), 0.27, 0.22, PAL["spid_chitin"]),
            ((0.0, 0.62, 0.06), 0.16, 0.13, PAL["spid_chitin"]),
            ((0.0, 0.72, 0.10), 0.055, 0.055, PAL["spid_band"]),
            ((0.0, 0.76, 0.20), 0.014, 0.014, PAL["spid_eye"]),
        ],
        segs=16,
    )
    # Karin ustu kirmizi-kehribar uyari seritleri (chevrons)
    for cy, cz, sx in [(0.22, 0.30, 0.16), (0.36, 0.34, 0.19), (0.50, 0.29, 0.14)]:
        pb.add_beveled_plate((0.0, cy, cz), (sx, 0.032, 0.022), PAL["spid_mark"], bevel_amt=0.008)
    parts.append(pb.finalize_object(mat, world_pos=(0.0, 0.10, 0.42)))

    # GERCEK 8 EKLEMLI ORUMCEK BACAGI (4 Sol + 4 Sag: Arachnid_Leg1..4_L/R, uclar z=-0.38 -> world z=0.0!)
    leg_Configs = [
        # (idx, coxa_y, yaw_deg, reach_r, arch_z)
        (1, -0.22, -44.0, 0.74, 0.36),
        (2, -0.09, -15.0, 0.80, 0.38),
        (3, 0.04, 16.0, 0.78, 0.37),
        (4, 0.16, 46.0, 0.76, 0.35),
    ]
    for idx, coxa_y, yaw_deg, reach_r, arch_z in leg_Configs:
        yaw_rad = math.radians(yaw_deg)
        dir_y = math.sin(yaw_rad)
        dir_x_abs = math.cos(yaw_rad)
        coxa_x_abs = 0.20 if idx in (1, 4) else 0.23

        for side_lbl, sgn in (("L", 1.0), ("R", -1.0)):
            pb = PartBuilder(f"Arachnid_Leg{idx}_{side_lbl}")
            dx = sgn * dir_x_abs
            dy = dir_y
            pb.add_curved_tube(
                [
                    ((0.0, 0.0, 0.0), 0.044, 0.044, PAL["spid_chitin"]),
                    ((dx * 0.14, dy * 0.14, 0.10), 0.038, 0.038, PAL["spid_band"]),
                    (
                        (dx * (reach_r * 0.46), dy * (reach_r * 0.46), arch_z),
                        0.034,
                        0.034,
                        PAL["spid_chitin"],
                    ),
                    (
                        (dx * (reach_r * 0.52), dy * (reach_r * 0.52), arch_z - 0.04),
                        0.032,
                        0.032,
                        PAL["spid_band"],
                    ),
                    ((dx * (reach_r * 0.84), dy * (reach_r * 0.84), -0.10), 0.022, 0.022, PAL["spid_chitin"]),
                    ((dx * reach_r, dy * reach_r, -0.38), 0.008, 0.008, PAL["spid_fang"]),
                ],
                segs=10,
            )
            parts.append(pb.finalize_object(mat, world_pos=(sgn * coxa_x_abs, coxa_y, 0.38)))

    sockets = create_gore_sockets(
        [
            ("Socket_GoreCap_Head", (0.0, -0.30, 0.40)),
            ("Socket_GoreCap_ArmL", (0.20, -0.22, 0.38)),
            ("Socket_GoreCap_ArmR", (-0.20, -0.22, 0.38)),
            ("Socket_GoreCap_LegL", (0.23, 0.04, 0.38)),
            ("Socket_GoreCap_LegR", (-0.23, 0.04, 0.38)),
        ]
    )
    views = [
        ("front", Vector((0.0, -2.75, 0.68)), Vector((0.0, 0.05, 0.38)), 48.0),
        ("side", Vector((-2.85, 0.10, 0.66)), Vector((0.0, 0.10, 0.38)), 48.0),
        ("top", Vector((0.0, 0.08, 3.10)), Vector((0.0, 0.10, 0.36)), 48.0),
        ("iso", Vector((-2.05, -2.15, 1.38)), Vector((0.0, 0.08, 0.38)), 48.0),
    ]
    return parts, sockets, views


# ==============================================================================
# 5. CREATURE_GRIFFIN_RAPTOR (Kanatli Kartal-Aslan Griffin / Yirtici Raptor)
# ==============================================================================
def build_griffin_raptor(mat):
    parts = []

    # Torso: Eagle breast front + Lion hindquarters rear (world pivot = 0, 0, 0.74)
    pb = PartBuilder("Griffin_Torso")
    pb.add_curved_tube(
        [
            ((0.0, 0.44, 0.02), 0.16, 0.17, PAL["grif_lion"]),
            ((0.0, 0.22, 0.04), 0.19, 0.20, PAL["grif_lion"]),
            ((0.0, -0.02, 0.04), 0.20, 0.22, PAL["grif_plume"]),
            ((0.0, -0.24, 0.08), 0.23, 0.26, PAL["grif_eagle"]),
            ((0.0, -0.38, 0.18), 0.16, 0.19, PAL["grif_eagle"]),
            ((0.0, -0.42, 0.26), 0.12, 0.14, PAL["grif_crest"]),
        ],
        segs=16,
    )
    # Gogus altin-krem tuy katmanlari
    for pz, py, rx in [(0.02, -0.34, 0.16), (0.12, -0.39, 0.14), (0.20, -0.41, 0.12)]:
        pb.add_ellipsoid((0.0, py, pz), (rx, 0.09, 0.08), PAL["grif_crest"], segs=10, rings=6)
    parts.append(pb.finalize_object(mat, world_pos=(0.0, 0.0, 0.74)))

    # Eagle Head + Hooked Golden Beak (world pivot = 0, -0.40, 0.98)
    pb = PartBuilder("Griffin_Head")
    pb.add_curved_tube(
        [
            ((0.0, 0.04, -0.02), 0.12, 0.12, PAL["grif_crest"]),
            ((0.0, -0.06, 0.03), 0.125, 0.12, PAL["grif_crest"]),
            ((0.0, -0.15, 0.02), 0.095, 0.095, PAL["grif_crest"]),
        ],
        segs=14,
    )
    # Kancali altin kartal gagasi (hooked raptor beak)
    pb.add_curved_tube(
        [
            ((0.0, -0.15, 0.01), 0.055, 0.058, PAL["grif_beak"]),
            ((0.0, -0.25, 0.00), 0.036, 0.045, PAL["grif_beak"]),
            ((0.0, -0.32, -0.035), 0.018, 0.028, PAL["grif_beak"]),
            ((0.0, -0.34, -0.085), 0.005, 0.008, PAL["dark_claw"]),
        ],
        segs=12,
    )
    for side in (-1.0, 1.0):
        # Keskin kartal gozleri + magrur kas kemeri + tuy kulak tutamlari
        pb.add_ellipsoid(
            (side * 0.104, -0.102, 0.040), (0.022, 0.026, 0.020), PAL["grif_beak"], segs=10, rings=6
        )
        pb.add_ellipsoid((side * 0.118, -0.110, 0.040), (0.010, 0.012, 0.012), PAL["pupil"], segs=8, rings=5)
        pb.add_beveled_plate(
            (side * 0.095, -0.102, 0.064),
            (0.026, 0.034, 0.010),
            PAL["grif_eagle"],
            rot_euler=(0.12, -side * 0.25, side * 0.12),
            bevel_amt=0.004,
        )
        pb.add_curved_tube(
            [
                ((side * 0.075, 0.00, 0.08), 0.026, 0.018, PAL["grif_crest"]),
                ((side * 0.115, 0.08, 0.15), 0.018, 0.012, PAL["grif_plume"]),
                ((side * 0.135, 0.16, 0.21), 0.006, 0.005, PAL["grif_eagle"]),
            ],
            segs=8,
        )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.40, 0.98)))

    # Lower Mandible (world pivot = 0, -0.50, 0.93)
    pb = PartBuilder("Griffin_Jaw")
    pb.add_curved_tube(
        [
            ((0.0, 0.02, 0.00), 0.042, 0.024, PAL["grif_crest"]),
            ((0.0, -0.09, -0.01), 0.030, 0.020, PAL["grif_beak"]),
            ((0.0, -0.17, -0.015), 0.012, 0.010, PAL["grif_beak"]),
        ],
        segs=10,
    )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.50, 0.93)))

    # 2-Segment Layered Feathered Wings (Inner + Outer)
    for side_lbl, sgn in (("L", 1.0), ("R", -1.0)):
        # Inner Wing (world pivot = sgn*0.20, -0.14, 0.86 -> outer joint rel (sgn*0.56, +0.04, +0.06))
        pb = PartBuilder(f"Griffin_WingInner_{side_lbl}")
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.00), 0.058, 0.052, PAL["grif_eagle"]),
                ((sgn * 0.28, -0.04, 0.05), 0.048, 0.042, PAL["grif_eagle"]),
                ((sgn * 0.56, 0.04, 0.06), 0.038, 0.034, PAL["grif_plume"]),
            ],
            segs=10,
        )
        # Ikincil telek tuyleri (7 adet ust uste binen kesintisiz katmanli tuy)
        for fi in range(7):
            u = fi / 6.0
            fx = sgn * (0.06 + u * 0.50)
            fy = -0.01 + u * 0.05
            fz = 0.02 + u * 0.03
            pb.add_ellipsoid(
                (fx, fy + 0.19, fz - 0.02),
                (0.062, 0.23, 0.018),
                PAL["grif_plume"] if fi % 2 == 0 else PAL["grif_eagle"],
                segs=10,
                rings=6,
                rot_euler=(-0.08, 0.0, -sgn * 0.10),
            )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.20, -0.14, 0.86)))

        # Outer Wing Pinions (world pivot = sgn*0.76, -0.10, 0.92)
        pb = PartBuilder(f"Griffin_WingOuter_{side_lbl}")
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.00), 0.036, 0.030, PAL["grif_plume"]),
                ((sgn * 0.34, 0.08, 0.02), 0.024, 0.020, PAL["grif_crest"]),
                ((sgn * 0.56, 0.22, -0.02), 0.010, 0.010, PAL["grif_crest"]),
            ],
            segs=10,
        )
        # Yelpaze gibi acilan 7 birincil ucus telekleri (ic kanattan baslayarak kesintisiz gecis)
        for pi in range(7):
            v = pi / 6.0
            px = sgn * (0.00 + v * 0.46)
            py = 0.02 + v * 0.17
            fan_yaw = sgn * (0.14 + v * 0.58)
            pb.add_ellipsoid(
                (px + sgn * 0.08, py + 0.19, -0.01),
                (0.052, 0.25 - v * 0.03, 0.015),
                PAL["grif_crest"] if pi >= 4 else PAL["grif_plume"],
                segs=10,
                rings=6,
                rot_euler=(-0.05, 0.0, -fan_yaw),
            )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.76, -0.10, 0.92)))

        # Eagle Front Talons (world pivot = sgn*0.18, -0.26, 0.66 -> lower at z=0.32 -> ground z=0.0)
        pb = PartBuilder(f"Griffin_FrontUpper_{side_lbl}")
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.05), 0.082, 0.095, PAL["grif_eagle"]),
                ((0.0, 0.00, -0.16), 0.066, 0.076, PAL["grif_plume"]),
                ((0.0, 0.00, -0.34), 0.046, 0.050, PAL["grif_beak"]),
            ],
            segs=12,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.18, -0.26, 0.66)))

        pb = PartBuilder(f"Griffin_FrontLower_{side_lbl}")
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.02), 0.044, 0.046, PAL["grif_beak"]),
                ((0.0, -0.01, -0.18), 0.034, 0.036, PAL["grif_beak"]),
                ((0.0, -0.03, -0.30), 0.042, 0.046, PAL["grif_beak"]),
            ],
            segs=10,
        )
        for tx in (-0.030, 0.0, 0.030):
            pb.add_curved_tube(
                [
                    ((tx, -0.04, -0.295), 0.014, 0.014, PAL["grif_beak"]),
                    ((tx * 1.25, -0.12, -0.305), 0.010, 0.010, PAL["grif_talon"]),
                    ((tx * 1.35, -0.16, -0.318), 0.004, 0.004, PAL["grif_talon"]),
                ],
                segs=8,
            )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.18, -0.26, 0.32)))

        # Lion Hind Legs (world pivot = sgn*0.19, 0.30, 0.68 -> lower at (sgn*0.19, 0.34, 0.32))
        pb = PartBuilder(f"Griffin_HindUpper_{side_lbl}")
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.05), 0.090, 0.112, PAL["grif_lion"]),
                ((0.0, 0.02, -0.18), 0.072, 0.088, PAL["grif_lion"]),
                ((0.0, 0.04, -0.36), 0.048, 0.054, PAL["grif_lion_shade"]),
            ],
            segs=12,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.19, 0.30, 0.68)))

        pb = PartBuilder(f"Griffin_HindLower_{side_lbl}")
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.02), 0.048, 0.052, PAL["grif_lion_shade"]),
                ((0.0, -0.03, -0.18), 0.038, 0.042, PAL["grif_lion"]),
                ((0.0, -0.07, -0.31), 0.052, 0.058, PAL["grif_lion"]),
            ],
            segs=12,
        )
        for tx in (-0.026, 0.0, 0.026):
            pb.add_ellipsoid((tx, -0.135, -0.305), (0.015, 0.026, 0.016), PAL["grif_talon"], segs=8, rings=5)
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.19, 0.34, 0.32)))

    # Fan Plume Tail (world pivot = 0, 0.44, 0.76)
    pb = PartBuilder("Griffin_Tail")
    pb.add_curved_tube(
        [
            ((0.0, -0.02, 0.00), 0.050, 0.050, PAL["grif_lion"]),
            ((0.0, 0.22, -0.06), 0.042, 0.042, PAL["grif_lion_shade"]),
            ((0.0, 0.44, -0.08), 0.036, 0.034, PAL["grif_eagle"]),
        ],
        segs=10,
    )
    for tf in (-1.0, 0.0, 1.0):
        pb.add_ellipsoid(
            (tf * 0.065, 0.54, -0.08),
            (0.048, 0.15, 0.016),
            PAL["grif_crest"] if tf == 0.0 else PAL["grif_plume"],
            segs=10,
            rings=6,
            rot_euler=(0.0, 0.0, -tf * 0.28),
        )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, 0.44, 0.76)))

    sockets = create_gore_sockets(
        [
            ("Socket_GoreCap_Head", (0.0, -0.40, 0.98)),
            ("Socket_GoreCap_ArmL", (0.20, -0.14, 0.86)),
            ("Socket_GoreCap_ArmR", (-0.20, -0.14, 0.86)),
            ("Socket_GoreCap_LegL", (0.19, 0.30, 0.68)),
            ("Socket_GoreCap_LegR", (-0.19, 0.30, 0.68)),
        ]
    )
    views = [
        ("front", Vector((0.0, -4.35, 1.02)), Vector((0.0, 0.00, 0.74)), 44.0),
        ("side", Vector((-3.55, 0.05, 0.94)), Vector((0.0, 0.05, 0.72)), 46.0),
        ("top", Vector((0.0, 0.00, 4.55)), Vector((0.0, 0.04, 0.70)), 44.0),
        ("iso", Vector((-2.95, -3.05, 1.85)), Vector((0.0, 0.02, 0.72)), 44.0),
    ]
    return parts, sockets, views


# ==============================================================================
# 6. CREATURE_ANCIENT_GOLEM (Kadim Runik Kaya / Demir Muhafiz Golemi — Biped)
# ==============================================================================
def build_ancient_golem(mat):
    parts = []

    # Torso (world pivot = 0, 0, 0.98)
    pb = PartBuilder("Golem_Torso")
    pb.add_curved_tube(
        [
            ((0.0, 0.00, -0.12), 0.22, 0.16, PAL["gol_stone_dark"]),
            ((0.0, 0.00, 0.04), 0.20, 0.15, PAL["gol_bronze"]),
            ((0.0, -0.01, 0.22), 0.27, 0.20, PAL["gol_stone"]),
            ((0.0, -0.02, 0.44), 0.35, 0.25, PAL["gol_stone"]),
            ((0.0, 0.00, 0.60), 0.28, 0.21, PAL["gol_stone_light"]),
        ],
        segs=12,
        power_x=0.72,
        power_n=0.76,
    )
    # Gogus zirhi tas bloklari + parlayan turkuaz runik cekirdek + yosun katmanlari
    pb.add_beveled_plate((0.0, -0.22, 0.42), (0.24, 0.07, 0.15), PAL["gol_stone_light"], bevel_amt=0.022)
    pb.add_beveled_plate(
        (0.0, -0.27, 0.42), (0.095, 0.035, 0.095), PAL["gol_bronze"], rot_euler=(0, 0.785, 0), bevel_amt=0.010
    )
    pb.add_ellipsoid((0.0, -0.295, 0.42), (0.062, 0.028, 0.062), PAL["gol_rune"], segs=10, rings=6)
    pb.add_beveled_plate((0.0, 0.18, 0.42), (0.22, 0.08, 0.16), PAL["gol_stone_dark"], bevel_amt=0.022)
    for side in (-1.0, 1.0):
        pb.add_ellipsoid((side * 0.22, 0.02, 0.58), (0.12, 0.11, 0.045), PAL["gol_moss"], segs=10, rings=6)
    parts.append(pb.finalize_object(mat, world_pos=(0.0, 0.0, 0.98)))

    # Head (world pivot = 0, -0.04, 1.62)
    pb = PartBuilder("Golem_Head")
    pb.add_beveled_plate((0.0, 0.00, 0.12), (0.15, 0.15, 0.14), PAL["gol_stone"], bevel_amt=0.028)
    pb.add_beveled_plate((0.0, -0.02, 0.26), (0.11, 0.13, 0.045), PAL["gol_bronze"], bevel_amt=0.012)
    # Runik parlayan vizor gozler
    pb.add_beveled_plate((0.0, -0.145, 0.13), (0.115, 0.022, 0.028), PAL["gol_stone_dark"], bevel_amt=0.006)
    for side in (-1.0, 1.0):
        pb.add_ellipsoid(
            (side * 0.055, -0.158, 0.13), (0.032, 0.014, 0.016), PAL["gol_rune"], segs=8, rings=5
        )
        pb.add_curved_tube(
            [
                ((side * 0.13, 0.00, 0.18), 0.036, 0.036, PAL["gol_bronze"]),
                ((side * 0.20, 0.02, 0.28), 0.024, 0.024, PAL["gol_stone_light"]),
                ((side * 0.21, 0.04, 0.37), 0.009, 0.009, PAL["gol_stone_light"]),
            ],
            segs=8,
        )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.04, 1.62)))

    # Jaw Guard (world pivot = 0, -0.10, 1.58)
    pb = PartBuilder("Golem_Jaw")
    pb.add_beveled_plate((0.0, -0.04, 0.00), (0.115, 0.085, 0.048), PAL["gol_stone_dark"], bevel_amt=0.014)
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.10, 1.58)))

    # Arms & Legs (feet resting solidly on world z = 0.0)
    for side_lbl, sgn in (("L", 1.0), ("R", -1.0)):
        # Upper Arm + Mossy Stone Pauldron (pivot = sgn*0.38, 0.0, 1.48 -> elbow rel (sgn*0.02, 0, -0.42))
        pb = PartBuilder(f"Golem_ArmUpper_{side_lbl}")
        pb.add_beveled_plate(
            (sgn * 0.04, 0.00, 0.06),
            (0.15, 0.16, 0.11),
            PAL["gol_stone_light"],
            rot_euler=(0.0, 0.0, -sgn * 0.18),
            bevel_amt=0.024,
        )
        pb.add_ellipsoid((sgn * 0.04, 0.02, 0.16), (0.11, 0.11, 0.035), PAL["gol_moss"], segs=10, rings=5)
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.00), 0.105, 0.105, PAL["gol_stone_dark"]),
                ((sgn * 0.01, 0.00, -0.22), 0.095, 0.095, PAL["gol_stone"]),
                ((sgn * 0.02, 0.00, -0.42), 0.088, 0.088, PAL["gol_bronze"]),
            ],
            segs=10,
            power_x=0.75,
            power_n=0.75,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.38, 0.0, 1.48)))

        # Forearm + Stone Crusher Gauntlet (world pivot = sgn*0.40, 0.0, 1.06)
        pb = PartBuilder(f"Golem_ArmLower_{side_lbl}")
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.02), 0.092, 0.092, PAL["gol_bronze"]),
                ((0.0, -0.02, -0.18), 0.125, 0.120, PAL["gol_stone"]),
                ((0.0, -0.03, -0.36), 0.135, 0.128, PAL["gol_stone_light"]),
                ((0.0, -0.03, -0.48), 0.105, 0.100, PAL["gol_stone_dark"]),
            ],
            segs=10,
            power_x=0.70,
            power_n=0.70,
        )
        pb.add_ellipsoid((sgn * 0.122, -0.02, -0.24), (0.022, 0.055, 0.085), PAL["gol_rune"], segs=8, rings=5)
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.40, 0.0, 1.06)))

        # Upper Leg (world pivot = sgn*0.20, 0.0, 0.88 -> knee rel (0, 0, -0.44))
        pb = PartBuilder(f"Golem_LegUpper_{side_lbl}")
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.04), 0.120, 0.128, PAL["gol_stone_dark"]),
                ((0.0, 0.00, -0.22), 0.112, 0.118, PAL["gol_stone"]),
                ((0.0, 0.00, -0.44), 0.098, 0.104, PAL["gol_bronze"]),
            ],
            segs=10,
            power_x=0.76,
            power_n=0.76,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.20, 0.0, 0.88)))

        # Lower Leg + Monolithic Foot on z = -0.44 (world z = 0.0)
        pb = PartBuilder(f"Golem_LegLower_{side_lbl}")
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.02), 0.102, 0.108, PAL["gol_bronze"]),
                ((0.0, 0.00, -0.22), 0.118, 0.124, PAL["gol_stone"]),
                ((0.0, -0.03, -0.38), 0.128, 0.145, PAL["gol_stone_light"]),
            ],
            segs=10,
            power_x=0.72,
            power_n=0.72,
        )
        pb.add_beveled_plate(
            (0.0, -0.055, -0.395), (0.125, 0.175, 0.045), PAL["gol_stone_dark"], bevel_amt=0.016
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.20, 0.0, 0.44)))

    sockets = create_gore_sockets(
        [
            ("Socket_GoreCap_Head", (0.0, -0.04, 1.62)),
            ("Socket_GoreCap_ArmL", (0.38, 0.0, 1.48)),
            ("Socket_GoreCap_ArmR", (-0.38, 0.0, 1.48)),
            ("Socket_GoreCap_LegL", (0.20, 0.0, 0.88)),
            ("Socket_GoreCap_LegR", (-0.20, 0.0, 0.88)),
        ]
    )
    views = [
        ("front", Vector((0.0, -3.55, 1.12)), Vector((0.0, 0.00, 0.98)), 48.0),
        ("side", Vector((-3.55, 0.00, 1.08)), Vector((0.0, 0.00, 0.96)), 48.0),
        ("top", Vector((0.0, -0.05, 4.10)), Vector((0.0, 0.00, 0.96)), 48.0),
        ("iso", Vector((-2.50, -2.60, 1.82)), Vector((0.0, 0.00, 0.98)), 48.0),
    ]
    return parts, sockets, views


def export_articulated_creature_glb(parts, sockets, out_glb_path):
    """
    Her parcanin kendi eklem pivot origin'ini (0,0,0) koruyarak tek .glb dosyasina ihrac eder
    (0 doku, UV-Free COLOR_0, +Y up); ardindan contact sheet render'i icin world_pos konumlarini geri yukler.
    """
    os.makedirs(os.path.dirname(out_glb_path), exist_ok=True)
    saved_locs = {obj.name: obj.location.copy() for obj in parts}
    for obj in parts:
        obj.location = Vector((0.0, 0.0, 0.0))

    bpy.ops.object.select_all(action="DESELECT")
    for obj in parts:
        obj.select_set(True)
    for emp in sockets:
        emp.select_set(True)

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

    for obj in parts:
        obj.location = saved_locs[obj.name]


def render_4_angle_contact_sheet(asset_id, views, out_png_path):
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

    world = bpy.data.worlds.new(f"PreviewWorld_{asset_id}")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs["Color"].default_value = (0.15, 0.18, 0.23, 1.0)
        bg.inputs["Strength"].default_value = 0.95

    lights = [
        ("KeyLight", (2.6, -3.6, 3.8), 180.0),
        ("FillLight", (-3.0, -2.4, 2.2), 85.0),
        ("RimLight", (0.3, 3.4, 3.0), 115.0),
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

    temp_paths = []
    for tag, pos, target, lens in views:
        cam_obj.location = pos
        cam_data.lens = lens
        direction = (target - pos).normalized()
        up_axis = "X" if tag == "top" else "Y"
        if tag == "top":
            cam_obj.rotation_euler = Euler((0.0, 0.0, 0.0), "XYZ")
        else:
            cam_obj.rotation_euler = direction.to_track_quat("-Z", up_axis).to_euler()
        out_p = os.path.join(OUT_PREVIEW_DIR, f"_tmp_{asset_id}_{tag}.png")
        scene.render.filepath = out_p
        bpy.ops.render.render(write_still=True)
        temp_paths.append(out_p)

    sheet = bpy.data.images.new(f"ContactSheet_{asset_id}", width=1024, height=1024, alpha=True)
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
    sheet.filepath_raw = out_png_path
    sheet.file_format = "PNG"
    sheet.save()
    bpy.data.images.remove(sheet)


CREATURE_SPECS = [
    ("creature_dire_wolf", build_dire_wolf),
    ("creature_horned_behemoth", build_horned_behemoth),
    ("creature_stag_steed", build_stag_steed),
    ("creature_arachnid_stalker", build_arachnid_stalker),
    ("creature_griffin_raptor", build_griffin_raptor),
    ("creature_ancient_golem", build_ancient_golem),
]


def main():
    os.makedirs(OUT_CREATURES_DIR, exist_ok=True)
    os.makedirs(OUT_PREVIEW_DIR, exist_ok=True)

    for asset_id, builder_fn in CREATURE_SPECS:
        clear_scene()
        mat = create_vertex_color_material(name=f"M_{asset_id}")
        parts, sockets, views = builder_fn(mat)
        total_tris = sum(len(o.data.polygons) for o in parts)
        out_glb = os.path.join(OUT_CREATURES_DIR, f"{asset_id}.glb")
        out_sheet = os.path.join(OUT_PREVIEW_DIR, f"{asset_id}_contact_sheet.png")
        export_articulated_creature_glb(parts, sockets, out_glb)
        render_4_angle_contact_sheet(asset_id, views, out_sheet)
        print(
            f"[CreatureAnatomicalBuilder] OK: {asset_id} -> {len(parts)} eklem parcasi, "
            f"{total_tris} ucgen | {os.path.relpath(out_glb, REPO_ROOT)}"
        )


if __name__ == "__main__":
    main()
