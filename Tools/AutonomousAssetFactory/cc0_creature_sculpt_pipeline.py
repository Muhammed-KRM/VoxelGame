"""
Stratocraft CC0 Organik Heykel & Anatomik 8-Yaratik GLB Uretim Hatti (Blender 4.2 LTS).

Kullanim (PATH uzerindeki blender veya python sarmalayicisi ile, SIFIR sabit mutlak yol):
    blender --background --python Tools/AutonomousAssetFactory/cc0_creature_sculpt_pipeline.py
    veya:
    python Tools/AutonomousAssetFactory/cc0_creature_sculpt_pipeline.py

Uretilen 8 Eklemli .glb Modeli (0 gomulu doku, UV-Free COLOR_0 dogrudan mat organik sRGB, R >= 22):
    1. Assets/Art/Generated/Creatures_21/creature_dire_wolf.glb
    2. Assets/Art/Generated/Creatures_21/creature_horned_behemoth.glb
    3. Assets/Art/Generated/Creatures_21/creature_stag_steed.glb
    4. Assets/Art/Generated/Creatures_21/creature_arachnid_stalker.glb (gercek 8 bacakli!)
    5. Assets/Art/Generated/Creatures_21/creature_griffin_raptor.glb
    6. Assets/Art/Generated/Creatures_21/creature_ancient_golem.glb (yalnizca #11/#17/#20 Golem/Automaton)
    7. Assets/Art/Generated/Creatures_21/creature_wild_boar.glb (YENI: Yaban Domuzu #2 - omuz kamburu, disler, toynaklar)
    8. Assets/Art/Generated/Creatures_21/creature_night_ghoul.glb (YENI: Gece Gulyabanisi #4 - kambur kaburga, uzun pence)
    + BuildLogs/Previews/creature_*_contact_sheet.png (8 adet 1024x1024 4-acili denetim karti)
"""

from __future__ import annotations

import math
import os
import shutil
import subprocess
import sys

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, "..", ".."))
OUT_CREATURES_DIR = os.path.join(REPO_ROOT, "Assets", "Art", "Generated", "Creatures_21")
OUT_PREVIEW_DIR = os.path.join(REPO_ROOT, "BuildLogs", "Previews")


def _run_via_blender_cli_if_needed() -> bool:
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

# Mat organik kurk / post / curuk ten / kemik / tas renk paleti (sRGB 0..255 -> COLOR_0; R >= 28)
PAL = {
    # 1. Dire Wolf (Gumus-boz kurk, arduvaz sirt yelesi, sicak krem gogus)
    "wolf_coat": (178, 172, 160, 255),
    "wolf_dorsal": (112, 116, 126, 255),
    "wolf_mane": (146, 138, 128, 255),
    "wolf_belly": (228, 222, 208, 255),
    "wolf_ear_in": (206, 168, 156, 255),
    "wolf_eye": (250, 192, 48, 255),
    # 2. Horned Behemoth (Kalin bizon/gergedan derisi + kayasi osteoderm zirh plakalari)
    "beh_hide": (166, 138, 114, 255),
    "beh_plate": (118, 108, 110, 255),
    "beh_belly": (196, 170, 144, 255),
    "beh_horn": (236, 222, 190, 255),
    "beh_horn_tip": (124, 106, 88, 255),
    "beh_eye": (250, 134, 42, 255),
    # 3. Stag Steed (Kizil-kestane savas ati/geyik kurku + mat deri eyer)
    "stag_coat": (196, 132, 82, 255),
    "stag_dark": (122, 80, 54, 255),
    "stag_belly": (232, 212, 184, 255),
    "stag_antler": (238, 222, 192, 255),
    "stag_hoof": (72, 62, 56, 255),
    "stag_tack": (116, 74, 48, 255),
    "stag_brass": (226, 184, 84, 255),
    # 4. Arachnid Stalker (8-bacakli mat kitin kabuk + kehribar-kizil uyari bantlari)
    "spid_chitin": (86, 80, 98, 255),
    "spid_carapace": (122, 110, 138, 255),
    "spid_band": (202, 142, 62, 255),
    "spid_mark": (224, 74, 54, 255),
    "spid_eye": (248, 52, 60, 255),
    "spid_fang": (60, 54, 66, 255),
    # 5. Griffin Raptor (Kartal telekleri + altin-aslan govdesi)
    "grif_eagle": (168, 112, 64, 255),
    "grif_plume": (210, 156, 88, 255),
    "grif_crest": (244, 236, 216, 255),
    "grif_lion": (218, 170, 98, 255),
    "grif_lion_shade": (180, 132, 70, 255),
    "grif_beak": (244, 194, 58, 255),
    "grif_talon": (62, 58, 66, 255),
    # 6. Ancient Golem (Yontulmus kadim kaya kutleleri + yosun + antik bronz kusaklar)
    "gol_stone": (156, 160, 166, 255),
    "gol_stone_dark": (112, 116, 124, 255),
    "gol_stone_light": (192, 196, 200, 255),
    "gol_moss": (106, 164, 84, 255),
    "gol_bronze": (196, 152, 84, 255),
    "gol_rune": (92, 232, 248, 255),
    # 7. Wild Boar (YENI: Mat koyu-kahve sert killi sirt, toprak-kahve kasli yan, acik boz karin, beyaz disler)
    "boar_bristle": (106, 76, 54, 255),
    "boar_coat": (158, 114, 78, 255),
    "boar_flank": (182, 136, 94, 255),
    "boar_belly": (212, 180, 144, 255),
    "boar_snout": (168, 118, 108, 255),
    "boar_hoof": (74, 62, 54, 255),
    "boar_eye": (246, 164, 42, 255),
    # 8. Night Ghoul (YENI: Soluk curuk kul-zeytin gergin deri, cokuk kaburga golgesi, acik kemik/omurga, kanli goz)
    "ghoul_skin": (150, 156, 136, 255),
    "ghoul_hollow": (102, 108, 96, 255),
    "ghoul_sinew": (140, 96, 90, 255),
    "ghoul_bone": (218, 208, 184, 255),
    "ghoul_claw": (232, 220, 192, 255),
    "ghoul_eye": (250, 98, 44, 255),
    # Ortak anatomik detaylar (R >= 32 -> asla siyah plastik siluet veya ruzgar salinimi olmaz)
    "ivory_fang": (246, 240, 224, 255),
    "mouth_dark": (124, 58, 60, 255),
    "dark_claw": (60, 54, 62, 255),
    "pupil": (34, 34, 40, 255),
    "sclera": (242, 244, 246, 255),
}


def srgb_u8_to_linear_rgb(rgba_u8: tuple[int, int, int, int]) -> tuple[float, float, float, float]:
    def _to_lin(c_byte: float) -> float:
        clamped = max(22.0, min(255.0, float(c_byte))) / 255.0
        return clamped / 12.92 if clamped <= 0.04045 else ((clamped + 0.055) / 1.055) ** 2.4

    return (
        _to_lin(rgba_u8[0]),
        _to_lin(rgba_u8[1]),
        _to_lin(rgba_u8[2]),
        max(0.0, min(1.0, rgba_u8[3] / 255.0)),
    )


def _lerp_rgba_u8(
    a: tuple[int, int, int, int], b: tuple[int, int, int, int], t: float
) -> tuple[int, int, int, int]:
    u = max(0.0, min(1.0, t))
    return (
        int(round(a[0] + (b[0] - a[0]) * u)),
        int(round(a[1] + (b[1] - a[1]) * u)),
        int(round(a[2] + (b[2] - a[2]) * u)),
        int(round(a[3] + (b[3] - a[3]) * u)),
    )


def clear_scene() -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)


def create_vertex_color_material(name: str = "M_CreatureMatteOrganic", roughness: float = 0.88):
    mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    vcol = nt.nodes.new("ShaderNodeVertexColor")
    vcol.layer_name = "Col"
    bsdf.inputs["Roughness"].default_value = roughness
    if "Specular IOR Level" in bsdf.inputs:
        bsdf.inputs["Specular IOR Level"].default_value = 0.14
    nt.links.new(vcol.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


def _organic_3d_grain(x: float, y: float, z: float, freq: float = 18.0) -> float:
    """3B uzayda deterministik mat kurk/deri/tas mikro-dokusu (-1..+1)."""
    p1 = math.sin(x * freq * 1.31 + math.cos(y * freq * 0.93) + z * freq * 1.17)
    p2 = math.cos(y * freq * 1.73 - math.sin(z * freq * 1.41) + x * freq * 0.89)
    return 0.5 * (p1 + p2)


class SculptPartBuilder:
    """
    Alt-bolumlenmis (Subdivision) ve organik kas/kurk/kemik yuzey yontmali eklem parcasi ureticisi.
    Yerel eklem pivotu = (0, 0, 0); Blender eksenleri: -Y = on (burun), +Z = yukari, +X = sol.
    """

    def __init__(self, name: str, sculpt_mode: str = "fur", subsurf_levels: int = 1):
        self.name = name
        self.sculpt_mode = sculpt_mode  # "fur", "boar", "ghoul", "feather", "chitin", "rock"
        self.subsurf_levels = subsurf_levels
        self.bm = bmesh.new()
        self.color_layer = self.bm.loops.layers.color.new("Col")

    def _compute_shaded_loop_color(
        self,
        pos: Vector,
        normal: Vector,
        base_rgba: tuple[int, int, int, int],
        belly_rgba: tuple[int, int, int, int] | None = None,
        dorsal_rgba: tuple[int, int, int, int] | None = None,
        cavity_ao: float = 1.0,
    ) -> tuple[float, float, float, float]:
        nz = normal.z if normal.length > 1e-5 else 0.0
        col_u8 = base_rgba
        if dorsal_rgba is not None and nz > 0.20:
            col_u8 = _lerp_rgba_u8(col_u8, dorsal_rgba, min(0.82, (nz - 0.20) * 0.95))
        elif belly_rgba is not None and nz < -0.18:
            col_u8 = _lerp_rgba_u8(col_u8, belly_rgba, min(0.78, (-nz - 0.18) * 0.90))

        grain = _organic_3d_grain(pos.x, pos.y, pos.z, 22.0 if self.sculpt_mode != "rock" else 11.0)
        shade = max(0.74, min(1.16, (0.95 + nz * 0.10 + grain * 0.065) * cavity_ao))
        shaded_u8 = (
            max(22, min(255, int(round(col_u8[0] * shade)))),
            max(20, min(255, int(round(col_u8[1] * shade)))),
            max(18, min(255, int(round(col_u8[2] * shade)))),
            int(max(185, min(255, round(248.0 * cavity_ao)))),
        )
        return srgb_u8_to_linear_rgb(shaded_u8)

    def _paint_faces_organic(
        self,
        faces: list,
        rgba_u8: tuple[int, int, int, int],
        belly_rgba: tuple[int, int, int, int] | None = None,
        dorsal_rgba: tuple[int, int, int, int] | None = None,
        cavity_ao: float = 1.0,
    ) -> None:
        for f in faces:
            if not f.is_valid:
                continue
            fn = f.normal
            for loop in f.loops:
                v = loop.vert
                vn = v.normal if v.normal.length > 1e-5 else fn
                blend_n = (fn * 0.45 + vn * 0.55).normalized()
                loop[self.color_layer] = self._compute_shaded_loop_color(
                    v.co, blend_n, rgba_u8, belly_rgba, dorsal_rgba, cavity_ao
                )

    def add_curved_tube(
        self,
        stations: list,
        segs: int = 16,
        cap_start: bool = True,
        cap_end: bool = True,
        power_x: float = 0.94,
        power_n: float = 0.94,
        dorsal_ridge: float = 0.0,
        muscle_lobes: float = 0.0,
        rib_grooves: float = 0.0,
        belly_rgba: tuple[int, int, int, int] | None = None,
        dorsal_rgba: tuple[int, int, int, int] | None = None,
    ) -> list:
        """
        Anatomik kas kesitli (omurga sirti, yan kas loblari, kaburga oluklari) kavisli organik uzuv/govde orer.
        """
        ring_verts = []
        ring_colors = []
        ring_ao = []
        count = len(stations)
        prev_axis_x = None

        radii_avg = [0.5 * (float(st[1]) + float(st[2])) for st in stations]
        max_r = max(radii_avg) if radii_avg else 0.1

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

            u_along = i / max(1, count - 1)
            pinch_ao = 0.88 + 0.12 * min(1.0, radii_avg[i] / max(0.01, max_r))
            rib_wave = math.sin(u_along * math.pi * 9.0) * rib_grooves if rib_grooves > 0.0 else 0.0

            rv = []
            for s in range(segs):
                a = (2.0 * math.pi * s) / segs
                ca = math.cos(a)
                sa = math.sin(a)
                sx = math.copysign(abs(ca) ** power_x, ca) * rx
                sn = math.copysign(abs(sa) ** power_n, sa) * ry

                # Anatomik omurga cikintisi (ust sirt hatti) + pektoral/deltoid/uyluk yan kas loblari
                if dorsal_ridge != 0.0 and sa > 0.25:
                    crest = ((sa - 0.25) / 0.75) ** 2.2
                    sn += dorsal_ridge * ry * crest
                    sx *= 1.0 - 0.16 * crest
                if muscle_lobes != 0.0:
                    lobe = math.sin(2.0 * a) ** 2 * math.sin(u_along * math.pi)
                    sx *= 1.0 + muscle_lobes * 0.14 * lobe
                    sn *= 1.0 + muscle_lobes * 0.08 * math.cos(2.0 * a)
                if rib_grooves > 0.0 and abs(ca) > 0.35 and sa > -0.45:
                    sx += rib_wave * rx * abs(ca)

                pt = center + axis_x * sx + axis_n * sn
                rv.append(self.bm.verts.new(pt))
            ring_verts.append(rv)
            ring_colors.append(rgba)
            ring_ao.append(pinch_ao)

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
            self.bm.normal_update()
            blended_col = _lerp_rgba_u8(ring_colors[r], ring_colors[r + 1], 0.5)
            band_ao = 0.5 * (ring_ao[r] + ring_ao[r + 1])
            self._paint_faces_organic(band_faces, blended_col, belly_rgba, dorsal_rgba, band_ao)

        if cap_start and count > 0:
            c0 = Vector(stations[0][0])
            v_start = self.bm.verts.new(c0)
            cap0_faces = []
            for s in range(segs):
                sn_idx = (s + 1) % segs
                try:
                    f0 = self.bm.faces.new((v_start, ring_verts[0][sn_idx], ring_verts[0][s]))
                    cap0_faces.append(f0)
                    all_faces.append(f0)
                except ValueError:
                    pass
            bmesh.ops.recalc_face_normals(self.bm, faces=cap0_faces)
            self._paint_faces_organic(cap0_faces, ring_colors[0], belly_rgba, dorsal_rgba, ring_ao[0])

        if cap_end and count > 0:
            c1 = Vector(stations[-1][0])
            v_end = self.bm.verts.new(c1)
            cap1_faces = []
            for s in range(segs):
                sn_idx = (s + 1) % segs
                try:
                    f1 = self.bm.faces.new((v_end, ring_verts[-1][s], ring_verts[-1][sn_idx]))
                    cap1_faces.append(f1)
                    all_faces.append(f1)
                except ValueError:
                    pass
            bmesh.ops.recalc_face_normals(self.bm, faces=cap1_faces)
            self._paint_faces_organic(cap1_faces, ring_colors[-1], belly_rgba, dorsal_rgba, ring_ao[-1])

        return all_faces

    def add_ellipsoid(
        self,
        center: tuple[float, float, float],
        radii: tuple[float, float, float],
        rgba_u8: tuple[int, int, int, int],
        segs: int = 12,
        rings: int = 8,
        rot_euler: tuple[float, float, float] = (0.0, 0.0, 0.0),
        cavity_ao: float = 0.98,
    ) -> list:
        rot_m = Euler(rot_euler, "XYZ").to_matrix().to_4x4()
        scale_m = Matrix.Diagonal((radii[0], radii[1], radii[2], 1.0))
        xform = Matrix.Translation(Vector(center)) @ rot_m @ scale_m
        ret = bmesh.ops.create_uvsphere(
            self.bm, u_segments=segs, v_segments=rings, radius=1.0, matrix=xform
        )
        verts = set(ret["verts"])
        faces = [f for f in self.bm.faces if all(v in verts for v in f.verts)]
        bmesh.ops.recalc_face_normals(self.bm, faces=faces)
        self.bm.normal_update()
        self._paint_faces_organic(faces, rgba_u8, cavity_ao=cavity_ao)
        return faces

    def add_beveled_plate(
        self,
        center: tuple[float, float, float],
        half_extents: tuple[float, float, float],
        rgba_u8: tuple[int, int, int, int],
        rot_euler: tuple[float, float, float] = (0.0, 0.0, 0.0),
        bevel_amt: float = 0.014,
    ) -> list:
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
            bmesh.ops.bevel(self.bm, geom=edges, offset=bevel_amt, segments=2, affect="EDGES")
            new_faces = [f for f in self.bm.faces if f not in prev_faces and f.is_valid]
        bmesh.ops.recalc_face_normals(self.bm, faces=new_faces)
        self.bm.normal_update()
        self._paint_faces_organic(new_faces, rgba_u8, cavity_ao=0.96)
        return new_faces

    def _apply_surface_sculpt_displacement(self, bm_target: bmesh.types.BMesh) -> None:
        """Subdivision sonrasi tepe noktalarina tur-ozel organik kurk/kas/kaburga/kaya yontma uygular."""
        bm_target.normal_update()
        for v in bm_target.verts:
            n = v.normal
            if n.length < 1e-5:
                continue
            p = v.co
            disp = 0.0
            if self.sculpt_mode == "boar":
                # Sert yaban domuzu kili ve omuz kas cizgileri
                bristle = math.sin(p.x * 46.0 + math.cos(p.y * 22.0)) * math.cos(p.z * 34.0)
                muscle = math.sin(p.y * 14.0 + p.z * 11.0)
                disp = 0.0045 * bristle + (0.0055 * abs(bristle) if n.z > 0.35 else 0.0025 * muscle)
            elif self.sculpt_mode == "ghoul":
                # Cokuk kaburga / lifli gergin kas / puruzlu nekrotik ten
                sinew = math.sin(p.z * 28.0 + p.y * 19.0) * math.cos(p.x * 24.0)
                disp = 0.0042 * sinew
            elif self.sculpt_mode == "fur":
                fur_flow = math.sin(p.x * 36.0 + math.sin(p.y * 18.0)) * math.cos(p.z * 28.0)
                disp = 0.0038 * fur_flow
            elif self.sculpt_mode == "feather":
                barb = math.sin(p.y * 34.0 + abs(p.x) * 26.0) * math.cos(p.z * 24.0)
                disp = 0.0032 * barb
            elif self.sculpt_mode == "chitin":
                seg = math.sin(p.y * 30.0 + p.x * 18.0)
                disp = 0.0024 * seg
            elif self.sculpt_mode == "rock":
                # Kirikli monolitik kaya fasetleri (Lego kutusu gorunumunu yok eder!)
                crag1 = math.sin(p.x * 15.0 + math.cos(p.z * 13.0)) * math.cos(p.y * 17.0)
                crag2 = math.cos(p.z * 27.0 - math.sin(p.x * 23.0))
                disp = 0.0105 * crag1 + 0.0045 * crag2

            v.co += n * disp

    def finalize_object(
        self,
        mat,
        world_pos: tuple[float, float, float] = (0.0, 0.0, 0.0),
        smooth_shading: bool = True,
        max_tris: int = 1800,
    ):
        base_mesh = bpy.data.meshes.new(f"{self.name}_raw")
        self.bm.to_mesh(base_mesh)
        self.bm.free()

        temp_obj = bpy.data.objects.new(f"{self.name}_tmp", base_mesh)
        bpy.context.collection.objects.link(temp_obj)
        bpy.context.view_layer.objects.active = temp_obj

        if self.subsurf_levels > 0:
            sub = temp_obj.modifiers.new(name="SC_OrganicSubsurf", type="SUBSURF")
            sub.subdivision_type = "CATMULL_CLARK"
            sub.levels = self.subsurf_levels
            sub.render_levels = self.subsurf_levels

        depsgraph = bpy.context.evaluated_depsgraph_get()
        eval_obj = temp_obj.evaluated_get(depsgraph)
        sculpt_mesh = bpy.data.meshes.new_from_object(eval_obj)

        bpy.data.objects.remove(temp_obj, do_unlink=True)
        bpy.data.meshes.remove(base_mesh, do_unlink=True)

        bm_sculpt = bmesh.new()
        bm_sculpt.from_mesh(sculpt_mesh)
        bpy.data.meshes.remove(sculpt_mesh, do_unlink=True)

        self._apply_surface_sculpt_displacement(bm_sculpt)
        bmesh.ops.triangulate(bm_sculpt, faces=bm_sculpt.faces[:])
        bmesh.ops.recalc_face_normals(bm_sculpt, faces=bm_sculpt.faces[:])

        final_mesh = bpy.data.meshes.new(self.name)
        bm_sculpt.to_mesh(final_mesh)
        bm_sculpt.free()

        obj = bpy.data.objects.new(self.name, final_mesh)
        bpy.context.collection.objects.link(obj)

        effective_cap = max(max_tris, 1400)
        if len(final_mesh.polygons) > effective_cap:
            ratio = max(0.35, effective_cap / float(len(final_mesh.polygons)))
            dec = obj.modifiers.new(name="SC_SilhouetteDecimate", type="DECIMATE")
            dec.decimate_type = "COLLAPSE"
            dec.ratio = ratio
            depsgraph2 = bpy.context.evaluated_depsgraph_get()
            eval_obj2 = obj.evaluated_get(depsgraph2)
            dec_mesh = bpy.data.meshes.new_from_object(eval_obj2)
            old_mesh = obj.data
            obj.modifiers.clear()
            obj.data = dec_mesh
            bpy.data.meshes.remove(old_mesh, do_unlink=True)
            dec_mesh.name = self.name

        for p in obj.data.polygons:
            p.use_smooth = smooth_shading

        obj.location = Vector(world_pos)
        obj.data.materials.append(mat)
        return obj


def create_gore_sockets(spec_list: list[tuple[str, tuple[float, float, float]]]) -> list:
    empties = []
    for sname, spos in spec_list:
        emp = bpy.data.objects.new(sname, None)
        emp.empty_display_size = 0.08
        emp.location = Vector(spos)
        bpy.context.collection.objects.link(emp)
        empties.append(emp)
    return empties


# ==============================================================================
# 1. CREATURE_DIRE_WOLF (Ulu Kurt — Kasli ve Yeleli Yirtici Dort-Ayakli)
# ==============================================================================
def build_dire_wolf(mat):
    parts = []

    pb = SculptPartBuilder("Wolf_Torso", sculpt_mode="fur", subsurf_levels=1)
    pb.add_curved_tube(
        [
            ((0.0, 0.45, 0.02), 0.12, 0.13, PAL["wolf_coat"]),
            ((0.0, 0.28, 0.04), 0.165, 0.18, PAL["wolf_coat"]),
            ((0.0, 0.08, 0.02), 0.15, 0.165, PAL["wolf_coat"]),
            ((0.0, -0.14, 0.05), 0.205, 0.245, PAL["wolf_mane"]),
            ((0.0, -0.32, 0.09), 0.19, 0.225, PAL["wolf_mane"]),
            ((0.0, -0.44, 0.14), 0.13, 0.155, PAL["wolf_dorsal"]),
        ],
        segs=16,
        dorsal_ridge=0.14,
        muscle_lobes=0.85,
        belly_rgba=PAL["wolf_belly"],
        dorsal_rgba=PAL["wolf_dorsal"],
    )
    # Katmanli sirt ve boyun yelesi kurk tutamlari
    for my, mz, rx, rz in [
        (-0.35, 0.23, 0.15, 0.095),
        (-0.21, 0.25, 0.145, 0.09),
        (-0.06, 0.20, 0.105, 0.065),
        (0.12, 0.19, 0.09, 0.055),
    ]:
        pb.add_curved_tube(
            [
                ((0.0, my - 0.06, mz - 0.02), rx, rz, PAL["wolf_mane"]),
                ((0.0, my + 0.05, mz + 0.04), rx * 0.65, rz * 0.55, PAL["wolf_dorsal"]),
                ((0.0, my + 0.13, mz + 0.02), 0.018, 0.015, PAL["wolf_dorsal"]),
            ],
            segs=10,
        )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, 0.0, 0.68), max_tris=950))

    pb = SculptPartBuilder("Wolf_Head", sculpt_mode="fur", subsurf_levels=1)
    pb.add_curved_tube(
        [
            ((0.0, 0.05, -0.02), 0.13, 0.13, PAL["wolf_mane"]),
            ((0.0, -0.05, 0.02), 0.142, 0.132, PAL["wolf_coat"]),
            ((0.0, -0.14, 0.01), 0.098, 0.092, PAL["wolf_coat"]),
            ((0.0, -0.24, -0.015), 0.064, 0.058, PAL["wolf_dorsal"]),
            ((0.0, -0.32, -0.020), 0.044, 0.042, PAL["wolf_dorsal"]),
        ],
        segs=14,
        belly_rgba=PAL["wolf_belly"],
        dorsal_rgba=PAL["wolf_dorsal"],
    )
    for side in (-1.0, 1.0):
        pb.add_curved_tube(
            [
                ((side * 0.09, -0.02, -0.01), 0.045, 0.055, PAL["wolf_coat"]),
                ((side * 0.135, 0.04, -0.035), 0.028, 0.038, PAL["wolf_belly"]),
                ((side * 0.145, 0.09, -0.045), 0.010, 0.012, PAL["wolf_belly"]),
            ],
            segs=8,
        )
        pb.add_curved_tube(
            [
                ((side * 0.086, 0.01, 0.095), 0.042, 0.028, PAL["wolf_dorsal"]),
                ((side * 0.104, 0.02, 0.185), 0.030, 0.020, PAL["wolf_coat"]),
                ((side * 0.112, 0.035, 0.265), 0.009, 0.008, PAL["wolf_dorsal"]),
            ],
            segs=10,
        )
        pb.add_ellipsoid(
            (side * 0.096, -0.005, 0.168), (0.018, 0.010, 0.044), PAL["wolf_ear_in"], segs=8, rings=5
        )
        pb.add_ellipsoid(
            (side * 0.092, -0.122, 0.048), (0.024, 0.026, 0.020), PAL["wolf_eye"], segs=10, rings=6
        )
        pb.add_ellipsoid((side * 0.108, -0.134, 0.048), (0.010, 0.012, 0.012), PAL["pupil"], segs=8, rings=5)
        pb.add_curved_tube(
            [
                ((side * 0.040, -0.255, -0.042), 0.012, 0.012, PAL["ivory_fang"]),
                ((side * 0.038, -0.262, -0.086), 0.003, 0.003, PAL["ivory_fang"]),
            ],
            segs=8,
        )
    pb.add_ellipsoid((0.0, -0.33, -0.012), (0.032, 0.024, 0.024), PAL["pupil"], segs=10, rings=6)
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.40, 0.80), max_tris=750))

    pb = SculptPartBuilder("Wolf_Jaw", sculpt_mode="fur", subsurf_levels=0)
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
                ((side * 0.030, -0.178, 0.026), 0.003, 0.003, PAL["ivory_fang"]),
            ],
            segs=8,
        )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.48, 0.74), max_tris=320))

    pb = SculptPartBuilder("Wolf_Tail", sculpt_mode="fur", subsurf_levels=1)
    pb.add_curved_tube(
        [
            ((0.0, -0.02, 0.00), 0.056, 0.056, PAL["wolf_dorsal"]),
            ((0.0, 0.14, -0.05), 0.078, 0.074, PAL["wolf_coat"]),
            ((0.0, 0.32, -0.14), 0.086, 0.080, PAL["wolf_coat"]),
            ((0.0, 0.48, -0.22), 0.062, 0.058, PAL["wolf_dorsal"]),
            ((0.0, 0.60, -0.25), 0.022, 0.022, PAL["wolf_belly"]),
        ],
        segs=12,
        belly_rgba=PAL["wolf_belly"],
        dorsal_rgba=PAL["wolf_dorsal"],
    )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, 0.42, 0.72), max_tris=360))

    for side_lbl, sgn in (("L", 1.0), ("R", -1.0)):
        pb = SculptPartBuilder(f"Wolf_FrontUpper_{side_lbl}", sculpt_mode="fur", subsurf_levels=1)
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.05), 0.082, 0.098, PAL["wolf_mane"]),
                ((0.0, 0.01, -0.12), 0.066, 0.078, PAL["wolf_coat"]),
                ((0.0, 0.03, -0.30), 0.046, 0.050, PAL["wolf_coat"]),
            ],
            segs=12,
            muscle_lobes=0.75,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.17, -0.26, 0.62), max_tris=320))

        pb = SculptPartBuilder(f"Wolf_FrontLower_{side_lbl}", sculpt_mode="fur", subsurf_levels=0)
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.02), 0.048, 0.050, PAL["wolf_coat"]),
                ((0.0, -0.01, -0.16), 0.036, 0.038, PAL["wolf_belly"]),
                ((0.0, -0.04, -0.27), 0.042, 0.046, PAL["wolf_belly"]),
                ((0.0, -0.08, -0.31), 0.052, 0.058, PAL["wolf_coat"]),
            ],
            segs=12,
        )
        for tx in (-0.026, 0.0, 0.026):
            pb.add_ellipsoid((tx, -0.125, -0.302), (0.016, 0.028, 0.018), PAL["wolf_belly"], segs=8, rings=5)
            pb.add_ellipsoid((tx, -0.152, -0.308), (0.007, 0.016, 0.009), PAL["dark_claw"], segs=6, rings=4)
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.17, -0.23, 0.32), max_tris=360))

        pb = SculptPartBuilder(f"Wolf_HindUpper_{side_lbl}", sculpt_mode="fur", subsurf_levels=1)
        pb.add_curved_tube(
            [
                ((0.0, -0.01, 0.06), 0.090, 0.116, PAL["wolf_coat"]),
                ((0.0, 0.02, -0.14), 0.074, 0.094, PAL["wolf_coat"]),
                ((0.0, 0.06, -0.32), 0.046, 0.054, PAL["wolf_dorsal"]),
            ],
            segs=12,
            muscle_lobes=0.85,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.18, 0.30, 0.64), max_tris=320))

        pb = SculptPartBuilder(f"Wolf_HindLower_{side_lbl}", sculpt_mode="fur", subsurf_levels=0)
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.02), 0.048, 0.054, PAL["wolf_dorsal"]),
                ((0.0, -0.04, -0.16), 0.035, 0.039, PAL["wolf_belly"]),
                ((0.0, -0.07, -0.28), 0.044, 0.048, PAL["wolf_belly"]),
                ((0.0, -0.11, -0.31), 0.052, 0.058, PAL["wolf_coat"]),
            ],
            segs=12,
        )
        for tx in (-0.026, 0.0, 0.026):
            pb.add_ellipsoid((tx, -0.150, -0.302), (0.016, 0.028, 0.018), PAL["wolf_belly"], segs=8, rings=5)
            pb.add_ellipsoid((tx, -0.176, -0.308), (0.007, 0.016, 0.009), PAL["dark_claw"], segs=6, rings=4)
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.18, 0.36, 0.32), max_tris=360))

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
# 2. CREATURE_HORNED_BEHEMOTH (Zirhli Boynuzlu Behemoth / Savas Ayisi / Yikici)
# ==============================================================================
def build_horned_behemoth(mat):
    parts = []

    pb = SculptPartBuilder("Behemoth_Torso", sculpt_mode="fur", subsurf_levels=1)
    pb.add_curved_tube(
        [
            ((0.0, 0.56, -0.02), 0.24, 0.23, PAL["beh_hide"]),
            ((0.0, 0.32, 0.04), 0.32, 0.30, PAL["beh_hide"]),
            ((0.0, 0.04, 0.08), 0.35, 0.34, PAL["beh_hide"]),
            ((0.0, -0.24, 0.15), 0.39, 0.39, PAL["beh_plate"]),
            ((0.0, -0.46, 0.10), 0.30, 0.31, PAL["beh_plate"]),
            ((0.0, -0.58, 0.04), 0.20, 0.21, PAL["beh_hide"]),
        ],
        segs=16,
        dorsal_ridge=0.18,
        muscle_lobes=0.95,
        belly_rgba=PAL["beh_belly"],
        dorsal_rgba=PAL["beh_plate"],
    )
    for py, pz, sx, sy in [
        (-0.28, 0.44, 0.21, 0.15),
        (-0.08, 0.40, 0.22, 0.15),
        (0.12, 0.34, 0.19, 0.14),
        (0.32, 0.26, 0.15, 0.12),
    ]:
        pb.add_ellipsoid(
            (0.0, py, pz), (sx, sy, 0.065), PAL["beh_plate"], segs=12, rings=6, rot_euler=(-0.16, 0, 0)
        )
        pb.add_curved_tube(
            [
                ((0.0, py, pz + 0.03), 0.045, 0.065, PAL["beh_horn_tip"]),
                ((0.0, py + 0.05, pz + 0.16), 0.012, 0.016, PAL["beh_horn"]),
            ],
            segs=8,
        )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, 0.0, 0.78), max_tris=980))

    pb = SculptPartBuilder("Behemoth_Head", sculpt_mode="fur", subsurf_levels=1)
    pb.add_curved_tube(
        [
            ((0.0, 0.06, 0.02), 0.21, 0.20, PAL["beh_plate"]),
            ((0.0, -0.10, 0.00), 0.22, 0.19, PAL["beh_plate"]),
            ((0.0, -0.26, -0.04), 0.17, 0.15, PAL["beh_hide"]),
            ((0.0, -0.38, -0.07), 0.13, 0.11, PAL["beh_hide"]),
        ],
        segs=14,
        belly_rgba=PAL["beh_belly"],
        dorsal_rgba=PAL["beh_plate"],
    )
    pb.add_beveled_plate(
        (0.0, -0.14, 0.14), (0.18, 0.15, 0.042), PAL["beh_plate"], rot_euler=(0.32, 0, 0), bevel_amt=0.016
    )
    pb.add_curved_tube(
        [
            ((0.0, -0.32, 0.00), 0.062, 0.072, PAL["beh_horn_tip"]),
            ((0.0, -0.40, 0.12), 0.040, 0.046, PAL["beh_horn"]),
            ((0.0, -0.43, 0.27), 0.010, 0.012, PAL["beh_horn"]),
        ],
        segs=10,
    )
    for side in (-1.0, 1.0):
        pb.add_curved_tube(
            [
                ((side * 0.16, -0.08, 0.10), 0.068, 0.068, PAL["beh_horn_tip"]),
                ((side * 0.30, -0.16, 0.14), 0.054, 0.054, PAL["beh_horn"]),
                ((side * 0.40, -0.30, 0.22), 0.038, 0.038, PAL["beh_horn"]),
                ((side * 0.36, -0.44, 0.35), 0.012, 0.012, PAL["ivory_fang"]),
            ],
            segs=12,
        )
        pb.add_ellipsoid(
            (side * 0.195, -0.175, 0.025), (0.026, 0.030, 0.022), PAL["beh_eye"], segs=10, rings=6
        )
        pb.add_ellipsoid((side * 0.212, -0.185, 0.025), (0.012, 0.014, 0.013), PAL["pupil"], segs=8, rings=5)
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.52, 0.82), max_tris=820))

    pb = SculptPartBuilder("Behemoth_Jaw", sculpt_mode="fur", subsurf_levels=0)
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
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.60, 0.72), max_tris=340))

    pb = SculptPartBuilder("Behemoth_Tail", sculpt_mode="fur", subsurf_levels=0)
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
    parts.append(pb.finalize_object(mat, world_pos=(0.0, 0.56, 0.78), max_tris=360))

    for side_lbl, sgn in (("L", 1.0), ("R", -1.0)):
        pb = SculptPartBuilder(f"Behemoth_FrontUpper_{side_lbl}", sculpt_mode="fur", subsurf_levels=1)
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.06), 0.145, 0.155, PAL["beh_plate"]),
                ((0.0, 0.00, -0.16), 0.125, 0.132, PAL["beh_hide"]),
                ((0.0, 0.00, -0.34), 0.102, 0.108, PAL["beh_hide"]),
            ],
            segs=14,
            muscle_lobes=0.80,
        )
        pb.add_beveled_plate(
            (sgn * 0.06, -0.02, -0.06), (0.09, 0.13, 0.13), PAL["beh_plate"], bevel_amt=0.016
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.28, -0.32, 0.66), max_tris=380))

        pb = SculptPartBuilder(f"Behemoth_FrontLower_{side_lbl}", sculpt_mode="fur", subsurf_levels=0)
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.02), 0.105, 0.110, PAL["beh_hide"]),
                ((0.0, -0.01, -0.18), 0.095, 0.100, PAL["beh_plate"]),
                ((0.0, -0.03, -0.31), 0.120, 0.128, PAL["beh_plate"]),
            ],
            segs=14,
        )
        for tx in (-0.065, 0.0, 0.065):
            pb.add_ellipsoid(
                (tx, -0.125, -0.292), (0.034, 0.052, 0.030), PAL["beh_horn_tip"], segs=8, rings=5
            )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.28, -0.32, 0.32), max_tris=380))

        pb = SculptPartBuilder(f"Behemoth_HindUpper_{side_lbl}", sculpt_mode="fur", subsurf_levels=1)
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.06), 0.150, 0.168, PAL["beh_hide"]),
                ((0.0, 0.01, -0.18), 0.125, 0.138, PAL["beh_hide"]),
                ((0.0, 0.00, -0.36), 0.102, 0.110, PAL["beh_plate"]),
            ],
            segs=14,
            muscle_lobes=0.85,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.27, 0.36, 0.68), max_tris=360))

        pb = SculptPartBuilder(f"Behemoth_HindLower_{side_lbl}", sculpt_mode="fur", subsurf_levels=0)
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.02), 0.102, 0.108, PAL["beh_plate"]),
                ((0.0, -0.01, -0.18), 0.092, 0.098, PAL["beh_plate"]),
                ((0.0, -0.03, -0.31), 0.118, 0.126, PAL["beh_plate"]),
            ],
            segs=14,
        )
        for tx in (-0.062, 0.0, 0.062):
            pb.add_ellipsoid(
                (tx, -0.120, -0.292), (0.032, 0.050, 0.030), PAL["beh_horn_tip"], segs=8, rings=5
            )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.27, 0.36, 0.32), max_tris=380))

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
# 3. CREATURE_STAG_STEED (Savas Ati / Asil Boynuzlu Geyik-At Binek)
# ==============================================================================
def build_stag_steed(mat):
    parts = []

    pb = SculptPartBuilder("Stag_Torso", sculpt_mode="fur", subsurf_levels=1)
    pb.add_curved_tube(
        [
            ((0.0, 0.48, 0.04), 0.17, 0.19, PAL["stag_coat"]),
            ((0.0, 0.26, 0.06), 0.205, 0.225, PAL["stag_coat"]),
            ((0.0, 0.00, 0.04), 0.195, 0.225, PAL["stag_coat"]),
            ((0.0, -0.26, 0.06), 0.22, 0.265, PAL["stag_coat"]),
            ((0.0, -0.38, 0.15), 0.15, 0.19, PAL["stag_coat"]),
            ((0.0, -0.44, 0.28), 0.11, 0.14, PAL["stag_coat"]),
            ((0.0, -0.46, 0.38), 0.095, 0.115, PAL["stag_dark"]),
        ],
        segs=16,
        dorsal_ridge=0.08,
        muscle_lobes=0.75,
        belly_rgba=PAL["stag_belly"],
        dorsal_rgba=PAL["stag_dark"],
    )
    pb.add_curved_tube(
        [
            ((0.0, -0.22, 0.26), 0.030, 0.058, PAL["stag_dark"]),
            ((0.0, -0.36, 0.33), 0.028, 0.054, PAL["stag_dark"]),
            ((0.0, -0.43, 0.43), 0.022, 0.044, PAL["stag_dark"]),
        ],
        segs=10,
    )
    pb.add_beveled_plate((0.0, -0.02, 0.23), (0.205, 0.19, 0.035), PAL["stag_tack"], bevel_amt=0.012)
    pb.add_beveled_plate((0.0, -0.02, 0.252), (0.210, 0.03, 0.018), PAL["stag_brass"], bevel_amt=0.006)
    parts.append(pb.finalize_object(mat, world_pos=(0.0, 0.0, 0.88), max_tris=920))

    pb = SculptPartBuilder("Stag_Head", sculpt_mode="fur", subsurf_levels=1)
    pb.add_curved_tube(
        [
            ((0.0, 0.04, 0.00), 0.105, 0.115, PAL["stag_coat"]),
            ((0.0, -0.08, -0.01), 0.098, 0.105, PAL["stag_coat"]),
            ((0.0, -0.22, -0.06), 0.066, 0.072, PAL["stag_coat"]),
            ((0.0, -0.33, -0.10), 0.048, 0.052, PAL["stag_dark"]),
        ],
        segs=14,
        belly_rgba=PAL["stag_belly"],
        dorsal_rgba=PAL["stag_dark"],
    )
    pb.add_ellipsoid((0.0, -0.345, -0.095), (0.038, 0.025, 0.026), PAL["pupil"], segs=10, rings=6)
    for side in (-1.0, 1.0):
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
        pb.add_curved_tube(
            [
                ((side * 0.115, 0.01, 0.18), 0.015, 0.015, PAL["stag_antler"]),
                ((side * 0.155, -0.10, 0.24), 0.011, 0.011, PAL["stag_antler"]),
                ((side * 0.145, -0.19, 0.30), 0.004, 0.004, PAL["ivory_fang"]),
            ],
            segs=8,
        )
        pb.add_curved_tube(
            [
                ((side * 0.215, 0.07, 0.34), 0.013, 0.013, PAL["stag_antler"]),
                ((side * 0.295, 0.11, 0.45), 0.009, 0.009, PAL["stag_antler"]),
                ((side * 0.310, 0.08, 0.55), 0.004, 0.004, PAL["ivory_fang"]),
            ],
            segs=8,
        )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.46, 1.24), max_tris=820))

    pb = SculptPartBuilder("Stag_Jaw", sculpt_mode="fur", subsurf_levels=0)
    pb.add_curved_tube(
        [
            ((0.0, 0.02, 0.00), 0.052, 0.028, PAL["stag_belly"]),
            ((0.0, -0.14, -0.04), 0.038, 0.022, PAL["stag_coat"]),
            ((0.0, -0.25, -0.06), 0.028, 0.018, PAL["stag_dark"]),
        ],
        segs=10,
    )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.52, 1.18), max_tris=220))

    pb = SculptPartBuilder("Stag_Tail", sculpt_mode="fur", subsurf_levels=0)
    pb.add_curved_tube(
        [
            ((0.0, -0.02, 0.00), 0.048, 0.048, PAL["stag_dark"]),
            ((0.0, 0.12, -0.08), 0.062, 0.056, PAL["stag_dark"]),
            ((0.0, 0.22, -0.26), 0.058, 0.050, PAL["stag_dark"]),
            ((0.0, 0.26, -0.42), 0.024, 0.022, PAL["stag_belly"]),
        ],
        segs=12,
    )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, 0.48, 0.94), max_tris=260))

    for side_lbl, sgn in (("L", 1.0), ("R", -1.0)):
        pb = SculptPartBuilder(f"Stag_FrontUpper_{side_lbl}", sculpt_mode="fur", subsurf_levels=1)
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.05), 0.078, 0.096, PAL["stag_coat"]),
                ((0.0, 0.00, -0.18), 0.056, 0.068, PAL["stag_coat"]),
                ((0.0, 0.00, -0.40), 0.042, 0.046, PAL["stag_dark"]),
            ],
            segs=12,
            muscle_lobes=0.70,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.17, -0.32, 0.80), max_tris=320))

        pb = SculptPartBuilder(f"Stag_FrontLower_{side_lbl}", sculpt_mode="fur", subsurf_levels=0)
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.02), 0.044, 0.046, PAL["stag_dark"]),
                ((0.0, 0.00, -0.22), 0.032, 0.035, PAL["stag_dark"]),
                ((0.0, -0.02, -0.34), 0.038, 0.042, PAL["stag_belly"]),
                ((0.0, -0.04, -0.395), 0.046, 0.054, PAL["stag_hoof"]),
            ],
            segs=12,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.17, -0.32, 0.40), max_tris=280))

        pb = SculptPartBuilder(f"Stag_HindUpper_{side_lbl}", sculpt_mode="fur", subsurf_levels=1)
        pb.add_curved_tube(
            [
                ((0.0, -0.01, 0.06), 0.090, 0.118, PAL["stag_coat"]),
                ((0.0, 0.02, -0.18), 0.068, 0.086, PAL["stag_coat"]),
                ((0.0, 0.05, -0.42), 0.044, 0.052, PAL["stag_dark"]),
            ],
            segs=12,
            muscle_lobes=0.80,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.18, 0.36, 0.82), max_tris=320))

        pb = SculptPartBuilder(f"Stag_HindLower_{side_lbl}", sculpt_mode="fur", subsurf_levels=0)
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.02), 0.044, 0.050, PAL["stag_dark"]),
                ((0.0, -0.03, -0.22), 0.032, 0.036, PAL["stag_dark"]),
                ((0.0, -0.05, -0.34), 0.038, 0.042, PAL["stag_belly"]),
                ((0.0, -0.07, -0.395), 0.046, 0.054, PAL["stag_hoof"]),
            ],
            segs=12,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.18, 0.41, 0.40), max_tris=280))

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
# 4. CREATURE_ARACHNID_STALKER (Gercek 8 Bacakli Kitin Orumcek / Akrep Avcisi)
# ==============================================================================
def build_arachnid_stalker(mat):
    parts = []

    pb = SculptPartBuilder("Arachnid_Torso", sculpt_mode="chitin", subsurf_levels=1)
    pb.add_curved_tube(
        [
            ((0.0, 0.14, 0.00), 0.10, 0.09, PAL["spid_chitin"]),
            ((0.0, 0.02, 0.03), 0.24, 0.16, PAL["spid_carapace"]),
            ((0.0, -0.14, 0.04), 0.26, 0.17, PAL["spid_carapace"]),
            ((0.0, -0.28, 0.02), 0.19, 0.13, PAL["spid_chitin"]),
            ((0.0, -0.35, 0.00), 0.12, 0.09, PAL["spid_chitin"]),
        ],
        segs=16,
        dorsal_ridge=0.10,
        dorsal_rgba=PAL["spid_carapace"],
    )
    pb.add_beveled_plate((0.0, -0.10, 0.16), (0.14, 0.18, 0.035), PAL["spid_band"], bevel_amt=0.010)
    parts.append(pb.finalize_object(mat, world_pos=(0.0, 0.0, 0.38), max_tris=620))

    pb = SculptPartBuilder("Arachnid_Head", sculpt_mode="chitin", subsurf_levels=0)
    pb.add_ellipsoid((0.0, -0.04, 0.02), (0.14, 0.10, 0.09), PAL["spid_chitin"], segs=12, rings=8)
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
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.30, 0.40), max_tris=520))

    pb = SculptPartBuilder("Arachnid_Jaw", sculpt_mode="chitin", subsurf_levels=0)
    for side in (-1.0, 1.0):
        pb.add_curved_tube(
            [
                ((side * 0.048, 0.00, 0.03), 0.036, 0.042, PAL["spid_carapace"]),
                ((side * 0.055, -0.07, -0.02), 0.024, 0.028, PAL["spid_fang"]),
                ((side * 0.025, -0.12, -0.09), 0.006, 0.006, PAL["spid_eye"]),
            ],
            segs=10,
        )
        pb.add_curved_tube(
            [
                ((side * 0.115, 0.03, 0.02), 0.024, 0.024, PAL["spid_chitin"]),
                ((side * 0.175, -0.08, 0.04), 0.022, 0.022, PAL["spid_band"]),
                ((side * 0.145, -0.20, -0.03), 0.016, 0.022, PAL["spid_chitin"]),
                ((side * 0.105, -0.25, -0.07), 0.006, 0.009, PAL["spid_fang"]),
            ],
            segs=10,
        )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.38, 0.34), max_tris=380))

    pb = SculptPartBuilder("Arachnid_Tail", sculpt_mode="chitin", subsurf_levels=1)
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
        rib_grooves=0.08,
    )
    for cy, cz, sx in [(0.22, 0.30, 0.16), (0.36, 0.34, 0.19), (0.50, 0.29, 0.14)]:
        pb.add_beveled_plate((0.0, cy, cz), (sx, 0.032, 0.022), PAL["spid_mark"], bevel_amt=0.008)
    parts.append(pb.finalize_object(mat, world_pos=(0.0, 0.10, 0.42), max_tris=720))

    leg_configs = [
        (1, -0.22, -44.0, 0.74, 0.36),
        (2, -0.09, -15.0, 0.80, 0.38),
        (3, 0.04, 16.0, 0.78, 0.37),
        (4, 0.16, 46.0, 0.76, 0.35),
    ]
    for idx, coxa_y, yaw_deg, reach_r, arch_z in leg_configs:
        yaw_rad = math.radians(yaw_deg)
        dir_y = math.sin(yaw_rad)
        dir_x_abs = math.cos(yaw_rad)
        coxa_x_abs = 0.20 if idx in (1, 4) else 0.23

        for side_lbl, sgn in (("L", 1.0), ("R", -1.0)):
            pb = SculptPartBuilder(f"Arachnid_Leg{idx}_{side_lbl}", sculpt_mode="chitin", subsurf_levels=0)
            dx = sgn * dir_x_abs
            dy = dir_y
            pb.add_curved_tube(
                [
                    ((0.0, 0.0, 0.0), 0.044, 0.044, PAL["spid_chitin"]),
                    ((dx * 0.14, dy * 0.14, 0.10), 0.038, 0.038, PAL["spid_band"]),
                    ((dx * (reach_r * 0.46), dy * (reach_r * 0.46), arch_z), 0.035, 0.035, PAL["spid_chitin"]),
                    ((dx * (reach_r * 0.52), dy * (reach_r * 0.52), arch_z - 0.04), 0.032, 0.032, PAL["spid_band"]),
                    ((dx * (reach_r * 0.84), dy * (reach_r * 0.84), -0.10), 0.022, 0.022, PAL["spid_chitin"]),
                    ((dx * reach_r, dy * reach_r, -0.38), 0.008, 0.008, PAL["spid_fang"]),
                ],
                segs=10,
            )
            parts.append(pb.finalize_object(mat, world_pos=(sgn * coxa_x_abs, coxa_y, 0.38), max_tris=240))

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
# 5. CREATURE_GRIFFIN_RAPTOR (Kanatli Kartal-Aslan Griffin / Wyvern Raptor)
# ==============================================================================
def build_griffin_raptor(mat):
    parts = []

    pb = SculptPartBuilder("Griffin_Torso", sculpt_mode="feather", subsurf_levels=1)
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
        muscle_lobes=0.80,
        belly_rgba=PAL["grif_crest"],
        dorsal_rgba=PAL["grif_eagle"],
    )
    for pz, py, rx in [(0.02, -0.34, 0.16), (0.12, -0.39, 0.14), (0.20, -0.41, 0.12)]:
        pb.add_ellipsoid((0.0, py, pz), (rx, 0.09, 0.08), PAL["grif_crest"], segs=10, rings=6)
    parts.append(pb.finalize_object(mat, world_pos=(0.0, 0.0, 0.74), max_tris=880))

    pb = SculptPartBuilder("Griffin_Head", sculpt_mode="feather", subsurf_levels=1)
    pb.add_curved_tube(
        [
            ((0.0, 0.04, -0.02), 0.12, 0.12, PAL["grif_crest"]),
            ((0.0, -0.06, 0.03), 0.125, 0.12, PAL["grif_crest"]),
            ((0.0, -0.15, 0.02), 0.095, 0.095, PAL["grif_crest"]),
        ],
        segs=14,
    )
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
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.40, 0.98), max_tris=680))

    pb = SculptPartBuilder("Griffin_Jaw", sculpt_mode="feather", subsurf_levels=0)
    pb.add_curved_tube(
        [
            ((0.0, 0.02, 0.00), 0.042, 0.024, PAL["grif_crest"]),
            ((0.0, -0.09, -0.01), 0.030, 0.020, PAL["grif_beak"]),
            ((0.0, -0.17, -0.015), 0.012, 0.010, PAL["grif_beak"]),
        ],
        segs=10,
    )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.50, 0.93), max_tris=220))

    for side_lbl, sgn in (("L", 1.0), ("R", -1.0)):
        pb = SculptPartBuilder(f"Griffin_WingInner_{side_lbl}", sculpt_mode="feather", subsurf_levels=0)
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.00), 0.058, 0.052, PAL["grif_eagle"]),
                ((sgn * 0.28, -0.04, 0.05), 0.048, 0.042, PAL["grif_eagle"]),
                ((sgn * 0.56, 0.04, 0.06), 0.038, 0.034, PAL["grif_plume"]),
            ],
            segs=10,
        )
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
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.20, -0.14, 0.86), max_tris=640))

        pb = SculptPartBuilder(f"Griffin_WingOuter_{side_lbl}", sculpt_mode="feather", subsurf_levels=0)
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.00), 0.036, 0.030, PAL["grif_plume"]),
                ((sgn * 0.34, 0.08, 0.02), 0.024, 0.020, PAL["grif_crest"]),
                ((sgn * 0.56, 0.22, -0.02), 0.010, 0.010, PAL["grif_crest"]),
            ],
            segs=10,
        )
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
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.76, -0.10, 0.92), max_tris=640))

        pb = SculptPartBuilder(f"Griffin_FrontUpper_{side_lbl}", sculpt_mode="feather", subsurf_levels=0)
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.05), 0.082, 0.095, PAL["grif_eagle"]),
                ((0.0, 0.00, -0.16), 0.066, 0.076, PAL["grif_plume"]),
                ((0.0, 0.00, -0.34), 0.046, 0.050, PAL["grif_beak"]),
            ],
            segs=12,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.18, -0.26, 0.66), max_tris=260))

        pb = SculptPartBuilder(f"Griffin_FrontLower_{side_lbl}", sculpt_mode="feather", subsurf_levels=0)
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
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.18, -0.26, 0.32), max_tris=360))

        pb = SculptPartBuilder(f"Griffin_HindUpper_{side_lbl}", sculpt_mode="fur", subsurf_levels=0)
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.05), 0.090, 0.112, PAL["grif_lion"]),
                ((0.0, 0.02, -0.18), 0.072, 0.088, PAL["grif_lion"]),
                ((0.0, 0.04, -0.36), 0.048, 0.054, PAL["grif_lion_shade"]),
            ],
            segs=12,
            muscle_lobes=0.75,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.19, 0.30, 0.68), max_tris=280))

        pb = SculptPartBuilder(f"Griffin_HindLower_{side_lbl}", sculpt_mode="fur", subsurf_levels=0)
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
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.19, 0.34, 0.32), max_tris=340))

    pb = SculptPartBuilder("Griffin_Tail", sculpt_mode="feather", subsurf_levels=0)
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
    parts.append(pb.finalize_object(mat, world_pos=(0.0, 0.44, 0.76), max_tris=360))

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
# 6. CREATURE_ANCIENT_GOLEM (Yontulmus Kaya/Demir Golem — YALNIZCA #11, #17, #20)
# ==============================================================================
def build_ancient_golem(mat):
    parts = []

    pb = SculptPartBuilder("Golem_Torso", sculpt_mode="rock", subsurf_levels=1)
    pb.add_curved_tube(
        [
            ((0.0, 0.00, -0.12), 0.22, 0.17, PAL["gol_stone_dark"]),
            ((0.0, 0.00, 0.04), 0.19, 0.15, PAL["gol_bronze"]),
            ((0.0, -0.02, 0.22), 0.28, 0.21, PAL["gol_stone"]),
            ((0.0, -0.03, 0.44), 0.36, 0.26, PAL["gol_stone"]),
            ((0.0, 0.01, 0.60), 0.27, 0.20, PAL["gol_stone_light"]),
        ],
        segs=12,
        power_x=0.76,
        power_n=0.78,
        dorsal_ridge=0.16,
        muscle_lobes=0.90,
        dorsal_rgba=PAL["gol_stone_light"],
        belly_rgba=PAL["gol_stone_dark"],
    )
    # Asimetrik dogal kaya omuz kutleleri + yosun katmanlari + antik bronz kemer
    for side in (-1.0, 1.0):
        pb.add_ellipsoid(
            (side * 0.22, -0.04, 0.46),
            (0.16, 0.15, 0.14),
            PAL["gol_stone_light"],
            segs=10,
            rings=6,
            rot_euler=(0.15 * side, -0.22, 0.18 * side),
        )
        pb.add_ellipsoid((side * 0.21, 0.02, 0.58), (0.12, 0.11, 0.045), PAL["gol_moss"], segs=10, rings=5)
    pb.add_ellipsoid((0.0, -0.25, 0.40), (0.075, 0.035, 0.075), PAL["gol_bronze"], segs=10, rings=6)
    pb.add_ellipsoid((0.0, -0.275, 0.40), (0.042, 0.022, 0.042), PAL["gol_rune"], segs=8, rings=5)
    parts.append(pb.finalize_object(mat, world_pos=(0.0, 0.0, 0.98), smooth_shading=False, max_tris=820))

    pb = SculptPartBuilder("Golem_Head", sculpt_mode="rock", subsurf_levels=1)
    pb.add_curved_tube(
        [
            ((0.0, 0.02, -0.02), 0.13, 0.13, PAL["gol_stone_dark"]),
            ((0.0, -0.01, 0.12), 0.16, 0.16, PAL["gol_stone"]),
            ((0.0, 0.01, 0.25), 0.13, 0.14, PAL["gol_stone_light"]),
            ((0.0, 0.03, 0.32), 0.07, 0.08, PAL["gol_moss"]),
        ],
        segs=10,
        power_x=0.78,
        power_n=0.78,
    )
    for side in (-1.0, 1.0):
        pb.add_ellipsoid(
            (side * 0.055, -0.152, 0.13), (0.028, 0.014, 0.014), PAL["gol_rune"], segs=8, rings=5
        )
        pb.add_curved_tube(
            [
                ((side * 0.12, 0.00, 0.18), 0.038, 0.038, PAL["gol_bronze"]),
                ((side * 0.19, 0.03, 0.29), 0.024, 0.024, PAL["gol_stone_light"]),
                ((side * 0.21, 0.05, 0.38), 0.009, 0.009, PAL["gol_stone_light"]),
            ],
            segs=8,
        )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.04, 1.62), smooth_shading=False, max_tris=520))

    pb = SculptPartBuilder("Golem_Jaw", sculpt_mode="rock", subsurf_levels=1)
    pb.add_curved_tube(
        [
            ((0.0, 0.02, 0.02), 0.11, 0.055, PAL["gol_stone_dark"]),
            ((0.0, -0.08, -0.01), 0.095, 0.052, PAL["gol_stone"]),
            ((0.0, -0.13, -0.02), 0.065, 0.040, PAL["gol_stone_dark"]),
        ],
        segs=9,
    )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.10, 1.58), smooth_shading=False, max_tris=220))

    for side_lbl, sgn in (("L", 1.0), ("R", -1.0)):
        pb = SculptPartBuilder(f"Golem_ArmUpper_{side_lbl}", sculpt_mode="rock", subsurf_levels=1)
        pb.add_ellipsoid(
            (sgn * 0.05, 0.00, 0.06), (0.16, 0.16, 0.13), PAL["gol_stone_light"], segs=10, rings=6
        )
        pb.add_ellipsoid((sgn * 0.05, 0.02, 0.16), (0.11, 0.11, 0.04), PAL["gol_moss"], segs=8, rings=5)
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.00), 0.105, 0.105, PAL["gol_stone_dark"]),
                ((sgn * 0.01, 0.00, -0.22), 0.096, 0.096, PAL["gol_stone"]),
                ((sgn * 0.02, 0.00, -0.42), 0.086, 0.086, PAL["gol_bronze"]),
            ],
            segs=10,
            power_x=0.78,
            power_n=0.78,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.38, 0.0, 1.48), smooth_shading=False, max_tris=420))

        pb = SculptPartBuilder(f"Golem_ArmLower_{side_lbl}", sculpt_mode="rock", subsurf_levels=1)
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.02), 0.092, 0.092, PAL["gol_bronze"]),
                ((0.0, -0.02, -0.18), 0.128, 0.122, PAL["gol_stone"]),
                ((0.0, -0.03, -0.36), 0.140, 0.132, PAL["gol_stone_light"]),
                ((0.0, -0.03, -0.48), 0.108, 0.102, PAL["gol_stone_dark"]),
            ],
            segs=10,
            power_x=0.75,
            power_n=0.75,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.40, 0.0, 1.06), smooth_shading=False, max_tris=380))

        pb = SculptPartBuilder(f"Golem_LegUpper_{side_lbl}", sculpt_mode="rock", subsurf_levels=1)
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.04), 0.122, 0.130, PAL["gol_stone_dark"]),
                ((0.0, 0.00, -0.22), 0.114, 0.120, PAL["gol_stone"]),
                ((0.0, 0.00, -0.44), 0.098, 0.104, PAL["gol_bronze"]),
            ],
            segs=10,
            power_x=0.78,
            power_n=0.78,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.20, 0.0, 0.88), smooth_shading=False, max_tris=340))

        pb = SculptPartBuilder(f"Golem_LegLower_{side_lbl}", sculpt_mode="rock", subsurf_levels=1)
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.02), 0.102, 0.108, PAL["gol_bronze"]),
                ((0.0, 0.00, -0.22), 0.118, 0.126, PAL["gol_stone"]),
                ((0.0, -0.04, -0.40), 0.132, 0.165, PAL["gol_stone_dark"]),
                ((0.0, -0.06, -0.44), 0.120, 0.155, PAL["gol_stone_dark"]),
            ],
            segs=10,
            power_x=0.76,
            power_n=0.76,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.20, 0.0, 0.44), smooth_shading=False, max_tris=360))

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


# ==============================================================================
# 7. CREATURE_WILD_BOAR (YENI — Yaban Domuzu #2: Yuksek Omuz Kamburu, Azi Disleri,
#    Sivri Kulaklar, Sert Killi Sirt ve Kasli Toynakli Bacaklar)
# ==============================================================================
def build_wild_boar(mat):
    parts = []

    # Torso (world pivot = 0, 0, 0.58): Yuksek omuz kamburu + Egimli arka sagri + Sert killi sirt (Razorback)
    pb = SculptPartBuilder("Boar_Torso", sculpt_mode="boar", subsurf_levels=1)
    pb.add_curved_tube(
        [
            ((0.0, 0.46, -0.02), 0.14, 0.15, PAL["boar_coat"]),
            ((0.0, 0.28, 0.02), 0.20, 0.21, PAL["boar_flank"]),
            ((0.0, 0.06, 0.05), 0.23, 0.25, PAL["boar_flank"]),
            ((0.0, -0.18, 0.11), 0.26, 0.31, PAL["boar_coat"]),
            ((0.0, -0.34, 0.13), 0.23, 0.29, PAL["boar_bristle"]),
            ((0.0, -0.46, 0.06), 0.17, 0.20, PAL["boar_coat"]),
        ],
        segs=18,
        dorsal_ridge=0.22,
        muscle_lobes=0.95,
        belly_rgba=PAL["boar_belly"],
        dorsal_rgba=PAL["boar_bristle"],
    )
    # Sirt boyunca sik ve kesintisiz sert koyu-kahve domuz kili sirti (razorback hackle crest)
    for hy, hz, hlen, hrad in [
        (-0.40, 0.34, 0.12, 0.058),
        (-0.32, 0.38, 0.14, 0.066),
        (-0.23, 0.40, 0.15, 0.070),
        (-0.14, 0.38, 0.14, 0.068),
        (-0.05, 0.35, 0.13, 0.062),
        (0.05, 0.31, 0.12, 0.056),
        (0.15, 0.27, 0.10, 0.048),
        (0.25, 0.22, 0.09, 0.042),
        (0.34, 0.16, 0.07, 0.034),
    ]:
        pb.add_curved_tube(
            [
                ((0.0, hy - 0.05, hz - 0.05), hrad, hrad * 0.90, PAL["boar_coat"]),
                ((0.0, hy + 0.02, hz + hlen * 0.55), hrad * 0.62, hrad * 0.55, PAL["boar_bristle"]),
                ((0.0, hy + 0.09, hz + hlen * 0.85), 0.012, 0.014, PAL["boar_bristle"]),
            ],
            segs=8,
        )
    # Omuz ve kalca kas gecis yastiklari (bacak eklemi dikisini sifirlar)
    for side in (-1.0, 1.0):
        pb.add_ellipsoid((side * 0.16, -0.24, -0.02), (0.095, 0.13, 0.12), PAL["boar_coat"], segs=10, rings=6)
        pb.add_ellipsoid((side * 0.16, 0.27, 0.00), (0.095, 0.13, 0.12), PAL["boar_flank"], segs=10, rings=6)
    parts.append(pb.finalize_object(mat, world_pos=(0.0, 0.0, 0.58), max_tris=1800))

    # Head (world pivot = 0, -0.44, 0.62): Kama kafatasi + Uzun kazici burun diski + Sivri kulaklar + Ust azi disleri
    pb = SculptPartBuilder("Boar_Head", sculpt_mode="boar", subsurf_levels=1)
    pb.add_curved_tube(
        [
            ((0.0, 0.05, 0.02), 0.165, 0.185, PAL["boar_bristle"]),
            ((0.0, -0.08, 0.01), 0.155, 0.165, PAL["boar_coat"]),
            ((0.0, -0.22, -0.04), 0.108, 0.105, PAL["boar_flank"]),
            ((0.0, -0.35, -0.08), 0.078, 0.072, PAL["boar_coat"]),
            ((0.0, -0.43, -0.09), 0.074, 0.068, PAL["boar_snout"]),
        ],
        segs=16,
        dorsal_ridge=0.12,
        belly_rgba=PAL["boar_belly"],
        dorsal_rgba=PAL["boar_bristle"],
    )
    # Yassi kikirdak domuz burun diski ve burun delikleri
    pb.add_ellipsoid(
        (0.0, -0.445, -0.088), (0.072, 0.024, 0.062), PAL["boar_snout"], segs=12, rings=6, rot_euler=(0.22, 0, 0)
    )
    for side in (-1.0, 1.0):
        pb.add_ellipsoid((side * 0.026, -0.464, -0.086), (0.014, 0.010, 0.018), PAL["pupil"], segs=8, rings=5)
        # Dik ve disa acili sivri killi domuz kulaklari
        pb.add_curved_tube(
            [
                ((side * 0.105, -0.01, 0.11), 0.046, 0.026, PAL["boar_coat"]),
                ((side * 0.165, 0.03, 0.21), 0.036, 0.018, PAL["boar_flank"]),
                ((side * 0.195, 0.06, 0.29), 0.010, 0.007, PAL["boar_bristle"]),
            ],
            segs=10,
        )
        pb.add_ellipsoid(
            (side * 0.145, 0.005, 0.19), (0.020, 0.010, 0.045), PAL["boar_snout"], segs=8, rings=5
        )
        # Kas kemeri altinda kizgin kehribar gozler
        pb.add_ellipsoid(
            (side * 0.128, -0.145, 0.035), (0.020, 0.024, 0.018), PAL["boar_eye"], segs=10, rings=6
        )
        pb.add_ellipsoid((side * 0.142, -0.152, 0.035), (0.009, 0.011, 0.010), PAL["pupil"], segs=8, rings=5)
        # Ust cene kivrik dis yan azi disleri (upper razors)
        pb.add_curved_tube(
            [
                ((side * 0.076, -0.31, -0.085), 0.022, 0.022, PAL["boar_snout"]),
                ((side * 0.122, -0.34, -0.055), 0.017, 0.017, PAL["ivory_fang"]),
                ((side * 0.148, -0.35, 0.015), 0.011, 0.011, PAL["ivory_fang"]),
                ((side * 0.142, -0.34, 0.075), 0.004, 0.004, PAL["ivory_fang"]),
            ],
            segs=10,
        )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.44, 0.62), max_tris=860))

    # Jaw + Buyuk Kivrik Beyaz Alt Azi Disleri (Tusks) (world pivot = 0, -0.52, 0.52)
    pb = SculptPartBuilder("Boar_Jaw", sculpt_mode="boar", subsurf_levels=0)
    pb.add_curved_tube(
        [
            ((0.0, 0.04, 0.01), 0.086, 0.044, PAL["boar_belly"]),
            ((0.0, -0.12, -0.015), 0.068, 0.038, PAL["boar_coat"]),
            ((0.0, -0.26, -0.030), 0.052, 0.030, PAL["boar_snout"]),
        ],
        segs=12,
    )
    for side in (-1.0, 1.0):
        # Belirgin yukari ve disa kivrilan fildisi yaban domuzu savas disleri (tusks)
        pb.add_curved_tube(
            [
                ((side * 0.054, -0.19, -0.015), 0.022, 0.022, PAL["ivory_fang"]),
                ((side * 0.108, -0.24, 0.035), 0.018, 0.018, PAL["ivory_fang"]),
                ((side * 0.136, -0.26, 0.115), 0.012, 0.012, PAL["ivory_fang"]),
                ((side * 0.124, -0.25, 0.185), 0.004, 0.004, PAL["ivory_fang"]),
            ],
            segs=10,
        )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.52, 0.52), max_tris=360))

    # Puskullu Kuyruk (world pivot = 0, 0.46, 0.60)
    pb = SculptPartBuilder("Boar_Tail", sculpt_mode="boar", subsurf_levels=0)
    pb.add_curved_tube(
        [
            ((0.0, -0.02, 0.00), 0.032, 0.032, PAL["boar_coat"]),
            ((0.0, 0.10, -0.06), 0.024, 0.024, PAL["boar_coat"]),
            ((0.0, 0.19, -0.15), 0.036, 0.032, PAL["boar_bristle"]),
            ((0.0, 0.24, -0.21), 0.012, 0.012, PAL["boar_bristle"]),
        ],
        segs=10,
    )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, 0.46, 0.60), max_tris=220))

    # Kasli Eklemli ve Cift Toynakli (Cloven Hoof) Bacaklar (world z = 0.0 temasli)
    for side_lbl, sgn in (("L", 1.0), ("R", -1.0)):
        # Front Upper (world pivot = sgn*0.17, -0.26, 0.50 -> knee rel (0, 0, -0.25))
        pb = SculptPartBuilder(f"Boar_FrontUpper_{side_lbl}", sculpt_mode="boar", subsurf_levels=1)
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.05), 0.088, 0.104, PAL["boar_coat"]),
                ((0.0, 0.01, -0.12), 0.068, 0.078, PAL["boar_flank"]),
                ((0.0, 0.00, -0.25), 0.048, 0.052, PAL["boar_coat"]),
            ],
            segs=12,
            muscle_lobes=0.85,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.17, -0.26, 0.50), max_tris=320))

        # Front Lower + Cift Toynak + Arka Mahmuz (world pivot = sgn*0.17, -0.26, 0.25 -> ground z=-0.25)
        pb = SculptPartBuilder(f"Boar_FrontLower_{side_lbl}", sculpt_mode="boar", subsurf_levels=0)
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.02), 0.048, 0.050, PAL["boar_coat"]),
                ((0.0, -0.01, -0.14), 0.036, 0.040, PAL["boar_bristle"]),
                ((0.0, -0.02, -0.21), 0.042, 0.046, PAL["boar_bristle"]),
            ],
            segs=12,
        )
        # Cift yarik toynak (cloven hooves) + arka mahmuz (dewclaw)
        for hx in (-0.019, 0.019):
            pb.add_curved_tube(
                [
                    ((hx, -0.02, -0.20), 0.020, 0.024, PAL["boar_hoof"]),
                    ((hx * 1.08, -0.065, -0.248), 0.018, 0.026, PAL["boar_hoof"]),
                ],
                segs=8,
            )
        pb.add_ellipsoid((0.0, 0.032, -0.185), (0.018, 0.016, 0.014), PAL["boar_hoof"], segs=6, rings=4)
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.17, -0.26, 0.25), max_tris=340))

        # Hind Upper (world pivot = sgn*0.17, 0.28, 0.52 -> hock rel (0, +0.04, -0.27))
        pb = SculptPartBuilder(f"Boar_HindUpper_{side_lbl}", sculpt_mode="boar", subsurf_levels=1)
        pb.add_curved_tube(
            [
                ((0.0, -0.01, 0.05), 0.096, 0.118, PAL["boar_flank"]),
                ((0.0, 0.02, -0.13), 0.074, 0.090, PAL["boar_coat"]),
                ((0.0, 0.04, -0.27), 0.046, 0.052, PAL["boar_bristle"]),
            ],
            segs=12,
            muscle_lobes=0.90,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.17, 0.28, 0.52), max_tris=320))

        # Hind Lower + Cift Toynak (world pivot = sgn*0.17, 0.32, 0.25 -> ground z=-0.25)
        pb = SculptPartBuilder(f"Boar_HindLower_{side_lbl}", sculpt_mode="boar", subsurf_levels=0)
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.02), 0.046, 0.050, PAL["boar_bristle"]),
                ((0.0, -0.02, -0.14), 0.035, 0.039, PAL["boar_bristle"]),
                ((0.0, -0.04, -0.21), 0.042, 0.046, PAL["boar_bristle"]),
            ],
            segs=12,
        )
        for hx in (-0.019, 0.019):
            pb.add_curved_tube(
                [
                    ((hx, -0.04, -0.20), 0.020, 0.024, PAL["boar_hoof"]),
                    ((hx * 1.08, -0.085, -0.248), 0.018, 0.026, PAL["boar_hoof"]),
                ],
                segs=8,
            )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.17, 0.32, 0.25), max_tris=340))

    sockets = create_gore_sockets(
        [
            ("Socket_GoreCap_Head", (0.0, -0.44, 0.62)),
            ("Socket_GoreCap_ArmL", (0.17, -0.26, 0.50)),
            ("Socket_GoreCap_ArmR", (-0.17, -0.26, 0.50)),
            ("Socket_GoreCap_LegL", (0.17, 0.28, 0.52)),
            ("Socket_GoreCap_LegR", (-0.17, 0.28, 0.52)),
        ]
    )
    views = [
        ("front", Vector((0.0, -2.45, 0.68)), Vector((0.0, -0.08, 0.48)), 50.0),
        ("side", Vector((-2.55, -0.02, 0.64)), Vector((0.0, -0.02, 0.46)), 48.0),
        ("top", Vector((0.0, -0.04, 2.75)), Vector((0.0, 0.00, 0.46)), 48.0),
        ("iso", Vector((-1.85, -1.95, 1.22)), Vector((0.0, -0.04, 0.48)), 48.0),
    ]
    return parts, sockets, views


# ==============================================================================
# 8. CREATURE_NIGHT_GHOUL (YENI — Gece Gulyabanisi #4 & Yirtici Bipedler:
#    Kambur Omurga, Cokuk Kaburgali Gogus, Uzun Penceli Eller, Korkutucu Ince Kafa)
# ==============================================================================
def build_night_ghoul(mat):
    parts = []

    # Torso (world pivot = 0, 0, 0.90): Ileri bukulmus kambur omurga + Cokuk kaburgali gogus + Sirt omur dikenleri
    pb = SculptPartBuilder("Ghoul_Torso", sculpt_mode="ghoul", subsurf_levels=1)
    pb.add_curved_tube(
        [
            ((0.0, 0.06, -0.10), 0.17, 0.13, PAL["ghoul_hollow"]),
            ((0.0, 0.04, 0.04), 0.135, 0.100, PAL["ghoul_hollow"]),
            ((0.0, 0.00, 0.20), 0.205, 0.150, PAL["ghoul_skin"]),
            ((0.0, -0.06, 0.35), 0.245, 0.175, PAL["ghoul_skin"]),
            ((0.0, -0.13, 0.45), 0.195, 0.145, PAL["ghoul_skin"]),
            ((0.0, -0.18, 0.50), 0.115, 0.100, PAL["ghoul_hollow"]),
        ],
        segs=16,
        dorsal_ridge=0.20,
        rib_grooves=0.10,
        belly_rgba=PAL["ghoul_hollow"],
        dorsal_rgba=PAL["ghoul_skin"],
    )
    # Koprucuk kemigi / kurek kemigi omuz yuvalari ve kalca kemeri ( sifir eklem boslugu! )
    for side in (-1.0, 1.0):
        pb.add_curved_tube(
            [
                ((side * 0.06, -0.10, 0.41), 0.055, 0.050, PAL["ghoul_bone"]),
                ((side * 0.21, -0.08, 0.39), 0.076, 0.072, PAL["ghoul_skin"]),
            ],
            segs=10,
        )
        pb.add_ellipsoid((side * 0.15, 0.05, -0.06), (0.085, 0.095, 0.085), PAL["ghoul_skin"], segs=10, rings=6)
    # 4 cift belirgin kaburga kemeri (sunken ribcage) ve kambur sirt omurga cikintilari
    for r_idx, (rz, ry, rw) in enumerate(
        [(0.16, -0.01, 0.185), (0.23, -0.03, 0.215), (0.30, -0.06, 0.235), (0.37, -0.09, 0.220)]
    ):
        for side in (-1.0, 1.0):
            pb.add_curved_tube(
                [
                    ((0.0, ry + 0.11, rz + 0.02), 0.018, 0.016, PAL["ghoul_bone"]),
                    ((side * rw, ry + 0.01, rz), 0.017, 0.016, PAL["ghoul_bone"]),
                    ((side * (rw * 0.52), ry - 0.14, rz - 0.025), 0.014, 0.013, PAL["ghoul_skin"]),
                ],
                segs=8,
            )
        pb.add_curved_tube(
            [
                ((0.0, ry + 0.12, rz + 0.03), 0.028, 0.032, PAL["ghoul_skin"]),
                ((0.0, ry + 0.22 + r_idx * 0.01, rz + 0.08), 0.009, 0.011, PAL["ghoul_bone"]),
            ],
            segs=7,
        )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, 0.0, 0.90), max_tris=1800))

    # Head (world pivot = 0, -0.18, 1.38): Korkutucu ince uzun kafatasi + Cokuk goz cukuru + Sivri kulaklar + Disler
    pb = SculptPartBuilder("Ghoul_Head", sculpt_mode="ghoul", subsurf_levels=1)
    pb.add_curved_tube(
        [
            ((0.0, 0.05, -0.02), 0.092, 0.105, PAL["ghoul_hollow"]),
            ((0.0, -0.03, 0.04), 0.104, 0.122, PAL["ghoul_skin"]),
            ((0.0, -0.12, 0.03), 0.088, 0.102, PAL["ghoul_skin"]),
            ((0.0, -0.20, -0.01), 0.068, 0.072, PAL["ghoul_bone"]),
            ((0.0, -0.25, -0.03), 0.050, 0.048, PAL["ghoul_hollow"]),
        ],
        segs=14,
        belly_rgba=PAL["ghoul_hollow"],
        dorsal_rgba=PAL["ghoul_bone"],
    )
    pb.add_ellipsoid((0.0, -0.255, -0.015), (0.018, 0.012, 0.024), PAL["pupil"], segs=8, rings=5)
    for side in (-1.0, 1.0):
        pb.add_ellipsoid(
            (side * 0.076, -0.135, 0.012), (0.026, 0.036, 0.018), PAL["ghoul_bone"], segs=8, rings=5
        )
        pb.add_ellipsoid(
            (side * 0.060, -0.168, 0.040), (0.024, 0.020, 0.022), PAL["pupil"], segs=8, rings=5
        )
        pb.add_ellipsoid(
            (side * 0.066, -0.180, 0.040), (0.013, 0.012, 0.012), PAL["ghoul_eye"], segs=8, rings=5
        )
        pb.add_curved_tube(
            [
                ((side * 0.088, -0.02, 0.045), 0.028, 0.018, PAL["ghoul_skin"]),
                ((side * 0.155, 0.06, 0.095), 0.020, 0.012, PAL["ghoul_sinew"]),
                ((side * 0.205, 0.13, 0.135), 0.006, 0.005, PAL["ghoul_hollow"]),
            ],
            segs=8,
        )
        for fx, fy, flen in [(0.038, -0.225, 0.048), (0.018, -0.242, 0.036)]:
            pb.add_curved_tube(
                [
                    ((side * fx, fy, -0.045), 0.009, 0.009, PAL["ghoul_claw"]),
                    ((side * fx, fy - 0.006, -0.045 - flen), 0.0025, 0.0025, PAL["ivory_fang"]),
                ],
                segs=6,
            )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.18, 1.38), max_tris=1400))

    # Jaw (world pivot = 0, -0.24, 1.31): Yirtik disli uzun alt cene
    pb = SculptPartBuilder("Ghoul_Jaw", sculpt_mode="ghoul", subsurf_levels=0)
    pb.add_curved_tube(
        [
            ((0.0, 0.03, 0.00), 0.062, 0.034, PAL["ghoul_sinew"]),
            ((0.0, -0.08, -0.025), 0.050, 0.028, PAL["ghoul_skin"]),
            ((0.0, -0.17, -0.040), 0.036, 0.024, PAL["ghoul_bone"]),
        ],
        segs=10,
    )
    for side in (-1.0, 1.0):
        pb.add_curved_tube(
            [
                ((side * 0.028, -0.15, -0.025), 0.008, 0.008, PAL["ghoul_claw"]),
                ((side * 0.030, -0.155, 0.012), 0.0025, 0.0025, PAL["ivory_fang"]),
            ],
            segs=6,
        )
    parts.append(pb.finalize_object(mat, world_pos=(0.0, -0.24, 1.31), max_tris=600))

    # Uzun Yirtici Kollar + Uzun Penceli Eller & Digitigrade Bacaklar (world z = 0.0 temasli)
    for side_lbl, sgn in (("L", 1.0), ("R", -1.0)):
        # Upper Arm (world pivot = sgn*0.21, -0.08, 1.28 -> elbow rel (sgn*0.05, -0.04, -0.36))
        pb = SculptPartBuilder(f"Ghoul_ArmUpper_{side_lbl}", sculpt_mode="ghoul", subsurf_levels=1)
        pb.add_curved_tube(
            [
                ((-sgn * 0.02, 0.00, 0.04), 0.076, 0.082, PAL["ghoul_skin"]),
                ((sgn * 0.02, -0.02, -0.18), 0.054, 0.058, PAL["ghoul_sinew"]),
                ((sgn * 0.05, -0.04, -0.36), 0.046, 0.050, PAL["ghoul_hollow"]),
            ],
            segs=12,
            muscle_lobes=0.75,
        )
        pb.add_curved_tube(
            [
                ((sgn * 0.02, 0.01, 0.04), 0.032, 0.028, PAL["ghoul_skin"]),
                ((sgn * 0.07, 0.03, 0.11), 0.009, 0.009, PAL["ghoul_bone"]),
            ],
            segs=7,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.21, -0.08, 1.28), max_tris=800))

        # Forearm + Uzun Penceli El (world pivot = sgn*0.26, -0.12, 0.92)
        pb = SculptPartBuilder(f"Ghoul_ArmLower_{side_lbl}", sculpt_mode="ghoul", subsurf_levels=0)
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.02), 0.046, 0.048, PAL["ghoul_hollow"]),
                ((sgn * 0.01, -0.04, -0.18), 0.042, 0.046, PAL["ghoul_skin"]),
                ((sgn * 0.01, -0.08, -0.34), 0.052, 0.034, PAL["ghoul_sinew"]),
            ],
            segs=10,
        )
        for c_idx, cx in enumerate((-0.036, -0.012, 0.012, 0.036)):
            clen = 0.145 if c_idx in (1, 2) else 0.120
            pb.add_curved_tube(
                [
                    ((sgn * 0.01 + cx, -0.085, -0.335), 0.013, 0.013, PAL["ghoul_bone"]),
                    ((sgn * 0.01 + cx * 1.18, -0.125, -0.335 - clen * 0.55), 0.009, 0.009, PAL["ghoul_claw"]),
                    ((sgn * 0.01 + cx * 1.25, -0.105, -0.335 - clen), 0.0025, 0.0025, PAL["ivory_fang"]),
                ],
                segs=7,
            )
        pb.add_curved_tube(
            [
                ((sgn * (-0.035), -0.075, -0.305), 0.012, 0.012, PAL["ghoul_bone"]),
                ((sgn * (-0.065), -0.115, -0.385), 0.003, 0.003, PAL["ivory_fang"]),
            ],
            segs=6,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.26, -0.12, 0.92), max_tris=900))

        # Upper Leg (world pivot = sgn*0.15, 0.05, 0.84 -> knee rel (0.0, -0.09, -0.40))
        pb = SculptPartBuilder(f"Ghoul_LegUpper_{side_lbl}", sculpt_mode="ghoul", subsurf_levels=1)
        pb.add_curved_tube(
            [
                ((-sgn * 0.01, 0.00, 0.05), 0.086, 0.098, PAL["ghoul_skin"]),
                ((0.0, -0.05, -0.20), 0.066, 0.074, PAL["ghoul_sinew"]),
                ((0.0, -0.09, -0.40), 0.048, 0.054, PAL["ghoul_hollow"]),
            ],
            segs=12,
            muscle_lobes=0.80,
        )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.15, 0.05, 0.84), max_tris=800))

        # Lower Leg + Penceli Digitigrade Ayak (world pivot = sgn*0.15, -0.04, 0.44 -> ground z=-0.44)
        pb = SculptPartBuilder(f"Ghoul_LegLower_{side_lbl}", sculpt_mode="ghoul", subsurf_levels=0)
        pb.add_curved_tube(
            [
                ((0.0, 0.00, 0.02), 0.048, 0.052, PAL["ghoul_hollow"]),
                ((0.0, 0.08, -0.20), 0.040, 0.046, PAL["ghoul_skin"]),
                ((0.0, 0.00, -0.39), 0.046, 0.052, PAL["ghoul_sinew"]),
                ((0.0, -0.07, -0.425), 0.056, 0.036, PAL["ghoul_skin"]),
            ],
            segs=12,
        )
        for tx in (-0.028, 0.0, 0.028):
            pb.add_curved_tube(
                [
                    ((tx, -0.09, -0.420), 0.013, 0.013, PAL["ghoul_bone"]),
                    ((tx * 1.18, -0.165, -0.438), 0.0035, 0.0035, PAL["ghoul_claw"]),
                ],
                segs=7,
            )
        parts.append(pb.finalize_object(mat, world_pos=(sgn * 0.15, -0.04, 0.44), max_tris=800))

    sockets = create_gore_sockets(
        [
            ("Socket_GoreCap_Head", (0.0, -0.18, 1.38)),
            ("Socket_GoreCap_ArmL", (0.21, -0.08, 1.28)),
            ("Socket_GoreCap_ArmR", (-0.21, -0.08, 1.28)),
            ("Socket_GoreCap_LegL", (0.15, 0.05, 0.84)),
            ("Socket_GoreCap_LegR", (-0.15, 0.05, 0.84)),
        ]
    )
    views = [
        ("front", Vector((0.0, -3.10, 0.96)), Vector((0.0, -0.06, 0.82)), 48.0),
        ("side", Vector((-3.15, -0.04, 0.92)), Vector((0.0, -0.04, 0.80)), 48.0),
        ("top", Vector((0.0, -0.06, 3.55)), Vector((0.0, -0.04, 0.80)), 48.0),
        ("iso", Vector((-2.20, -2.30, 1.55)), Vector((0.0, -0.05, 0.82)), 48.0),
    ]
    return parts, sockets, views


def export_articulated_creature_glb(parts: list, sockets: list, out_glb_path: str) -> None:
    """
    Her eklem parcasinin kendi yerel pivot origin'ini (0,0,0) koruyarak tek .glb dosyasina ihrac eder
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


def render_4_angle_contact_sheet(asset_id: str, views: list, out_png_path: str) -> None:
    """4 acili 512x512 render alir ve 1024x1024 PNG contact sheet olarak kaydeder."""
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
        bg.inputs["Color"].default_value = (0.16, 0.19, 0.24, 1.0)
        bg.inputs["Strength"].default_value = 1.05

    lights = [
        ("KeyLight", (2.6, -3.6, 3.8), 195.0),
        ("FillLight", (-3.0, -2.4, 2.2), 95.0),
        ("RimLight", (0.3, 3.4, 3.0), 110.0),
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
    ("creature_dire_wolf", build_dire_wolf, 0.88),
    ("creature_horned_behemoth", build_horned_behemoth, 0.86),
    ("creature_stag_steed", build_stag_steed, 0.84),
    ("creature_arachnid_stalker", build_arachnid_stalker, 0.72),
    ("creature_griffin_raptor", build_griffin_raptor, 0.85),
    ("creature_ancient_golem", build_ancient_golem, 0.62),
    ("creature_wild_boar", build_wild_boar, 0.90),
    ("creature_night_ghoul", build_night_ghoul, 0.88),
]


def main() -> None:
    os.makedirs(OUT_CREATURES_DIR, exist_ok=True)
    os.makedirs(OUT_PREVIEW_DIR, exist_ok=True)

    for asset_id, builder_fn, mat_rough in CREATURE_SPECS:
        clear_scene()
        mat = create_vertex_color_material(name=f"M_{asset_id}", roughness=mat_rough)
        parts, sockets, views = builder_fn(mat)
        total_tris = sum(len(o.data.polygons) for o in parts)
        out_glb = os.path.join(OUT_CREATURES_DIR, f"{asset_id}.glb")
        out_sheet = os.path.join(OUT_PREVIEW_DIR, f"{asset_id}_contact_sheet.png")
        export_articulated_creature_glb(parts, sockets, out_glb)
        render_4_angle_contact_sheet(asset_id, views, out_sheet)
        print(
            f"[CC0CreatureSculptPipeline] OK: {asset_id} -> {len(parts)} eklem parcasi, "
            f"{total_tris} ucgen | {os.path.relpath(out_glb, REPO_ROOT)}"
        )


if __name__ == "__main__":
    main()
