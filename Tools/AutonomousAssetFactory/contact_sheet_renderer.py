"""Stratocraft 4-Acili (On, Yan, Ust, Izometrik) 512x512 Kontrol Gorseli Ureticisi."""

from __future__ import annotations

import shutil
import subprocess
from pathlib import Path

import numpy as np
import pygltflib
from PIL import Image, ImageDraw

from asset_validator import _read_accessor_array


def _project_triangles_panel(
    vertices: np.ndarray,
    faces: np.ndarray,
    face_colors: np.ndarray,
    view_mode: str,
    panel_size: int = 256,
) -> Image.Image:
    """Deterministik arkadan-one (Painter's Algorithm) golgeli 256x256 panel cizer."""
    img = Image.new("RGB", (panel_size, panel_size), (24, 28, 36))
    draw = ImageDraw.Draw(img)

    # Izgara cizgileri
    mid = panel_size // 2
    draw.line([(0, mid), (panel_size, mid)], fill=(40, 46, 58), width=1)
    draw.line([(mid, 0), (mid, panel_size)], fill=(40, 46, 58), width=1)

    if len(vertices) == 0 or len(faces) == 0:
        return img

    v = vertices.copy()
    center = (v.min(axis=0) + v.max(axis=0)) * 0.5
    v = v - center
    extent = float(np.max(v.max(axis=0) - v.min(axis=0)))
    scale = (panel_size * 0.70) / max(extent, 1e-4)

    if view_mode == "FRONT":
        px, py, depth = v[:, 0], v[:, 1], v[:, 2]
    elif view_mode == "SIDE":
        px, py, depth = v[:, 2], v[:, 1], -v[:, 0]
    elif view_mode == "TOP":
        px, py, depth = v[:, 0], -v[:, 2], v[:, 1]
    else:  # ISOMETRIC
        yaw = np.deg2rad(38.0)
        pitch = np.deg2rad(28.0)
        cy, sy = np.cos(yaw), np.sin(yaw)
        cp, sp = np.cos(pitch), np.sin(pitch)
        rx = v[:, 0] * cy - v[:, 2] * sy
        rz = v[:, 0] * sy + v[:, 2] * cy
        ry = v[:, 1] * cp - rz * sp
        rz2 = v[:, 1] * sp + rz * cp
        px, py, depth = rx, ry, rz2

    sx = px * scale + mid
    sy = mid - py * scale

    tri_depths = depth[faces].mean(axis=1)
    order = np.argsort(tri_depths)
    light_dir = np.array([0.45, 0.75, 0.48], dtype=np.float32)
    light_dir /= np.linalg.norm(light_dir)

    for fi in order:
        f = faces[fi]
        p0, p1, p2 = vertices[f[0]], vertices[f[1]], vertices[f[2]]
        nrm = np.cross(p1 - p0, p2 - p0)
        norm_len = float(np.linalg.norm(nrm))
        if norm_len > 1e-8:
            nrm = nrm / norm_len
        lambert = 0.42 + 0.58 * max(0.0, float(np.dot(nrm, light_dir)))
        base_rgb = face_colors[fi]
        lit_rgb = tuple(int(np.clip(c * lambert, 0, 255)) for c in base_rgb)
        pts = [
            (float(sx[f[0]]), float(sy[f[0]])),
            (float(sx[f[1]]), float(sy[f[1]])),
            (float(sx[f[2]]), float(sy[f[2]])),
        ]
        draw.polygon(pts, fill=lit_rgb, outline=(16, 18, 22))

    draw.rectangle([(0, 0), (panel_size - 1, panel_size - 1)], outline=(72, 82, 102), width=1)
    draw.text((8, 6), view_mode, fill=(215, 225, 240))
    return img


def _material_id_to_preview_rgb(mat_id: int) -> tuple[int, int, int]:
    """Kontrol kartinda farkli MaterialID parcalarini ayirt edici fiziksel tonda gosterir."""
    palette = {
        1: (128, 132, 138),   # Stone
        30: (198, 114, 68),   # Copper
        32: (155, 160, 168),  # Iron
        35: (232, 192, 68),   # Gold
        37: (118, 198, 232),  # Mithril
        41: (156, 112, 238),  # Astral Crystal
        51: (172, 182, 196),  # Steel
        60: (144, 156, 174),  # Damascus Steel
        70: (152, 106, 64),   # OakWood
        71: (108, 76, 52),    # Ironwood
    }
    if mat_id in palette:
        return palette[mat_id]
    return ((mat_id * 53) % 180 + 60, (mat_id * 97) % 180 + 60, (mat_id * 149) % 180 + 60)


def render_4angle_contact_sheet(
    glb_path: Path,
    out_png_path: Path,
    prefer_blender_cli: bool = False,
) -> Path:
    """On (+Z), Yan (+X), Ust (+Y) ve Izometrik 4-acili 512x512 PNG kontrol gorseli uretir."""
    out_png_path.parent.mkdir(parents=True, exist_ok=True)

    blender_exe = shutil.which("blender")
    if prefer_blender_cli and blender_exe:
        expr = (
            "import bpy; bpy.ops.wm.read_factory_settings(use_empty=True); "
            f"bpy.ops.import_scene.gltf(filepath=r'{glb_path}'); "
            "bpy.context.scene.render.resolution_x=512; bpy.context.scene.render.resolution_y=512; "
            f"bpy.context.scene.render.filepath=r'{out_png_path}'; "
            "cam=bpy.data.cameras.new('C'); obj=bpy.data.objects.new('C',cam); "
            "bpy.context.scene.collection.objects.link(obj); bpy.context.scene.camera=obj; "
            "obj.location=(1.6, -1.8, 1.4); obj.rotation_euler=(1.05, 0.0, 0.72); "
            "bpy.ops.render.render(write_still=True)"
        )
        completed = subprocess.run(
            [blender_exe, "--background", "--python-expr", expr],
            capture_output=True,
            text=True,
            check=False,
        )
        if completed.returncode == 0 and out_png_path.exists():
            return out_png_path

    # Deterministik < 0.05 sn 4-panelli (512x512) yazilimsal golgeli kontrol karti
    gltf = pygltflib.GLTF2().load(str(glb_path))
    blob = gltf.binary_blob() or b""

    verts_list: list[np.ndarray] = []
    faces_list: list[np.ndarray] = []
    fcols_list: list[np.ndarray] = []
    v_offset = 0

    for mesh in gltf.meshes:
        for prim in mesh.primitives:
            pos = _read_accessor_array(gltf, blob, prim.attributes.POSITION).astype(np.float32)
            if prim.indices is not None:
                idx = _read_accessor_array(gltf, blob, prim.indices).astype(np.int32).reshape((-1, 3))
            else:
                idx = np.arange(len(pos), dtype=np.int32).reshape((-1, 3))

            if prim.attributes.COLOR_0 is not None:
                cols = _read_accessor_array(gltf, blob, prim.attributes.COLOR_0)
                mat_ids = cols[:, 0].astype(np.int32)
                tri_mats = mat_ids[idx[:, 0]]
                tri_rgb = np.array([_material_id_to_preview_rgb(int(m)) for m in tri_mats], dtype=np.uint8)
            else:
                tri_rgb = np.full((len(idx), 3), 180, dtype=np.uint8)

            verts_list.append(pos)
            faces_list.append(idx + v_offset)
            fcols_list.append(tri_rgb)
            v_offset += len(pos)

    all_v = np.vstack(verts_list) if verts_list else np.zeros((0, 3), dtype=np.float32)
    all_f = np.vstack(faces_list) if faces_list else np.zeros((0, 3), dtype=np.int32)
    all_c = np.vstack(fcols_list) if fcols_list else np.zeros((0, 3), dtype=np.uint8)

    sheet = Image.new("RGB", (512, 512), (18, 20, 26))
    panels = [
        ("FRONT (+Z)", "FRONT", (0, 0)),
        ("SIDE (+X)", "SIDE", (256, 0)),
        ("TOP (+Y)", "TOP", (0, 256)),
        ("ISO 3D", "ISO", (256, 256)),
    ]
    for label, mode, pos_xy in panels:
        panel_img = _project_triangles_panel(all_v, all_f, all_c, view_mode=mode, panel_size=256)
        draw = ImageDraw.Draw(panel_img)
        draw.text((92, 6), f"| {glb_path.stem[:20]} ({label})", fill=(150, 205, 165))
        sheet.paste(panel_img, pos_xy)

    sheet.save(out_png_path, format="PNG")
    return out_png_path
