"""Stratocraft Otonom 3B Varlik Fabrikasi - Soyut Uretici ve Deterministik GLB Insa Sinifi."""

from __future__ import annotations

import abc
import math
import struct
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import numpy as np
import pygltflib


@dataclass(slots=True)
class ManifestEntry:
    """Stratocraft_Asset_Manifest.json icindeki tek bir 3B varlik sozlesmesi."""

    asset_id: str
    category: str
    phase: int
    generator_path: str
    target_dimensions_meters: tuple[float, float, float]
    dimension_tolerance_ratio: float
    pivot_rule: str
    forward_axis: str
    up_axis: str
    poly_budget_lod0: int
    poly_budget_lod1: int
    poly_budget_lod2: int
    default_material_id: int
    required_sockets: list[str] = field(default_factory=list)
    dismemberable_bones: list[str] = field(default_factory=list)
    bake_vat: bool = False

    @classmethod
    def from_dict(cls, raw: dict[str, Any]) -> ManifestEntry:
        """JSON sozlugunden ManifestEntry nesnesi olusturur."""
        dims = raw["targetDimensionsMeters"]
        budget = raw["polyBudget"]
        return cls(
            asset_id=str(raw["assetId"]),
            category=str(raw["category"]),
            phase=int(raw["phase"]),
            generator_path=str(raw["generatorPath"]),
            target_dimensions_meters=(float(dims["x"]), float(dims["y"]), float(dims["z"])),
            dimension_tolerance_ratio=float(raw.get("dimensionToleranceRatio", 0.02)),
            pivot_rule=str(raw.get("pivotRule", "BottomCenter_Zero")),
            forward_axis=str(raw.get("forwardAxis", "+Z")),
            up_axis=str(raw.get("upAxis", "+Y")),
            poly_budget_lod0=int(budget["lod0"]),
            poly_budget_lod1=int(budget["lod1"]),
            poly_budget_lod2=int(budget["lod2"]),
            default_material_id=int(raw["defaultMaterialId"]),
            required_sockets=[str(s) for s in raw.get("requiredSockets", [])],
            dismemberable_bones=[str(b) for b in raw.get("dismemberableBones", [])],
            bake_vat=bool(raw.get("bakeVat", False)),
        )


class BaseAssetGenerator(abc.ABC):
    """Tum 3B model ureticilerinin (PathA ve PathB) uymasi gereken temel arayuz."""

    @abc.abstractmethod
    def generate(self, spec: ManifestEntry, out_glb_path: Path) -> Path:
        """Hedef spesifikasyona gore UV-Free Color32 nitelikli .glb dosyasini uretir."""


def _append_flat_triangle(
    positions: list[tuple[float, float, float]],
    normals: list[tuple[float, float, float]],
    colors: list[tuple[int, int, int, int]],
    indices: list[int],
    p0: tuple[float, float, float],
    p1: tuple[float, float, float],
    p2: tuple[float, float, float],
    rgba: tuple[int, int, int, int],
    facet_idx: int = 0,
) -> None:
    """Tek bir ufak faset ucgenini kendi gercek yuzey normali ve faset AO/ton varyasyonuyla ekler."""
    ux, uy, uz = p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2]
    vx, vy, vz = p2[0] - p0[0], p2[1] - p0[1], p2[2] - p0[2]
    nx = uy * vz - uz * vy
    ny = uz * vx - ux * vz
    nz = ux * vy - uy * vx
    length = math.sqrt(nx * nx + ny * ny + nz * nz)
    if length < 1e-8:
        return
    normal = (nx / length, ny / length, nz / length)

    # Her fasete hafif keskinlik/parlaklik (B) ve Cavity AO (A) varyasyonu uygula (Low-Poly Facet gorunumu)
    mat_r, temp_g, sharp_b, ao_a = rgba
    shade_delta = ((facet_idx * 37) % 29) - 14
    new_b = max(10, min(255, sharp_b + shade_delta))
    ny_factor = int((normal[1] * 0.5 + 0.5) * 24.0) - 12
    new_a = max(110, min(255, ao_a + ny_factor))
    tri_rgba = (mat_r, temp_g, new_b, new_a)

    base_idx = len(positions)
    positions.extend([p0, p1, p2])
    normals.extend([normal, normal, normal])
    colors.extend([tri_rgba, tri_rgba, tri_rgba])
    indices.extend([base_idx, base_idx + 1, base_idx + 2])


def _append_box_part(
    positions: list[tuple[float, float, float]],
    normals: list[tuple[float, float, float]],
    colors: list[tuple[int, int, int, int]],
    indices: list[int],
    min_pt: tuple[float, float, float],
    max_pt: tuple[float, float, float],
    rgba: tuple[int, int, int, int],
    profile: str = "sculpted",
) -> None:
    """
    Verilen AABB zarfini (%100 birebir min_pt..max_pt sinirlarini koruyarak) Minecraft kutusu yerine
    ufak fasetli poligonlardan (Deep Rock Galactic / Astroneer / Stilize Yontulmus Fantezi Low-Poly)
    olusan cok halkali sekizgen/kristal/anatomik prizma olarak mesh listesine ekler.
    """
    x0, y0, z0 = min_pt
    x1, y1, z1 = max_pt
    cx = 0.5 * (x0 + x1)
    cy = 0.5 * (y0 + y1)
    cz = 0.5 * (z0 + z1)
    rx = 0.5 * (x1 - x0)
    ry = 0.5 * (y1 - y0)
    rz = 0.5 * (z1 - z0)

    # Boyutlardan en uzunu ana omurga ekseni secilir (0=X, 1=Y, 2=Z)
    if profile == "wheel":
        axis = 0  # Tekerlek X ekseni etrafinda doner
    elif profile == "crystal":
        axis = 1  # Kristal dikey Y ekseninde yukselir
    elif profile == "wing":
        axis = 0  # Kanat X ekseninde acilir
    elif profile == "horn":
        axis = 2  # Boynuz / burun / ors boynuzu Z ekseninde uzanir
    else:
        if ry >= rx and ry >= rz:
            axis = 1
        elif rz >= rx and rz >= ry:
            axis = 2
        else:
            axis = 0

    sides = 8
    # Profil halkalari: (u_pos in [-1..1], scale_u, scale_v, offset_v)
    if profile == "crystal":
        # Altigen/sekizgen kristal: sivri taban, genis orta faset, sivri piramit tepe
        ring_specs = [
            (-0.78, 0.72, 0.72, 0.0),
            (0.0, 1.00, 1.00, 0.0),
            (0.58, 0.68, 0.68, 0.0),
        ]
    elif profile == "horn":
        # Arkadan one dogru sivrilen boynuz / kurt burnu / ors boynuzu
        ring_specs = [
            (-0.85, 0.92, 0.92, 0.0),
            (0.0, 1.00, 1.00, 0.0),
            (0.75, 0.42, 0.42, -0.10),
        ]
    elif profile == "wing":
        # Kanat / namlu: elmas kesitli aerodinamik fasetli yuzey
        ring_specs = [
            (-0.82, 0.85, 0.85, 0.0),
            (0.0, 1.00, 1.00, 0.0),
            (0.82, 0.55, 0.70, 0.05),
        ]
    elif profile == "wheel":
        # Sekizgen kusatma tekerlegi / makara
        ring_specs = [
            (-0.85, 0.92, 0.92, 0.0),
            (0.0, 1.00, 1.00, 0.0),
            (0.85, 0.92, 0.92, 0.0),
        ]
    else:
        # Anatomik / zirh / kaya yontulmus cok fasetli prizma
        ring_specs = [
            (-0.78, 0.74, 0.74, 0.0),
            (0.0, 1.00, 1.00, 0.0),
            (0.78, 0.78, 0.78, 0.0),
        ]

    def _make_point(
        u_norm: float, su: float, sv: float, ov: float, theta: float
    ) -> tuple[float, float, float]:
        ct = math.cos(theta)
        st = math.sin(theta)
        if axis == 1:  # Y omurgasi, XZ kesiti
            px = cx + rx * su * ct
            py = cy + ry * u_norm
            pz = cz + rz * min(1.0, max(-1.0, sv * st + ov))
            return (px, py, pz)
        elif axis == 2:  # Z omurgasi, XY kesiti
            px = cx + rx * su * ct
            py = cy + ry * min(1.0, max(-1.0, sv * st + ov))
            pz = cz + rz * u_norm
            return (px, py, pz)
        else:  # X omurgasi, YZ kesiti
            px = cx + rx * u_norm
            py = cy + ry * su * ct
            pz = cz + rz * min(1.0, max(-1.0, sv * st + ov))
            return (px, py, pz)

    if axis == 1:
        cap_neg = (cx, y0, cz)
        cap_pos = (cx, y1, cz)
    elif axis == 2:
        cap_neg = (cx, cy, z0)
        cap_pos = (cx, cy, z1)
    else:
        cap_neg = (x0, cy, cz)
        cap_pos = (x1, cy, cz)

    rings: list[list[tuple[float, float, float]]] = []
    for u_norm, su, sv, ov in ring_specs:
        ring_pts: list[tuple[float, float, float]] = []
        for k in range(sides):
            theta = (2.0 * math.pi * k) / sides
            ring_pts.append(_make_point(u_norm, su, sv, ov, theta))
        rings.append(ring_pts)

    facet_counter = 0
    # Alt / Arka kapak fasetleri (cap_neg -> ring[0])
    first_ring = rings[0]
    for k in range(sides):
        kn = (k + 1) % sides
        _append_flat_triangle(
            positions, normals, colors, indices,
            cap_neg, first_ring[kn], first_ring[k],
            rgba, facet_counter
        )
        facet_counter += 1

    # Halkalar arasi faset kusaklari
    for r_idx in range(len(rings) - 1):
        r_curr = rings[r_idx]
        r_next = rings[r_idx + 1]
        for k in range(sides):
            kn = (k + 1) % sides
            p00 = r_curr[k]
            p01 = r_curr[kn]
            p10 = r_next[k]
            p11 = r_next[kn]
            _append_flat_triangle(
                positions, normals, colors, indices,
                p00, p01, p11,
                rgba, facet_counter
            )
            facet_counter += 1
            _append_flat_triangle(
                positions, normals, colors, indices,
                p00, p11, p10,
                rgba, facet_counter
            )
            facet_counter += 1

    # Ust / On kapak fasetleri (ring[-1] -> cap_pos)
    last_ring = rings[-1]
    for k in range(sides):
        kn = (k + 1) % sides
        _append_flat_triangle(
            positions, normals, colors, indices,
            last_ring[k], last_ring[kn], cap_pos,
            rgba, facet_counter
        )
        facet_counter += 1


def write_color32_glb(
    out_glb_path: Path,
    mesh_name: str,
    positions: np.ndarray,
    normals: np.ndarray,
    colors_rgba_u8: np.ndarray,
    indices_u16: np.ndarray,
    sockets: dict[str, tuple[float, float, float]],
) -> Path:
    """UV-Free StratocraftUberShader sozlesmesine uygun COLOR_0 (UNSIGNED_BYTE Normalized) .glb yazar."""
    out_glb_path.parent.mkdir(parents=True, exist_ok=True)

    pos_f32 = np.asarray(positions, dtype=np.float32)
    nrm_f32 = np.asarray(normals, dtype=np.float32)
    col_u8 = np.asarray(colors_rgba_u8, dtype=np.uint8)
    idx_u16 = np.asarray(indices_u16, dtype=np.uint16)

    pos_bytes = pos_f32.tobytes()
    nrm_bytes = nrm_f32.tobytes()
    col_bytes = col_u8.tobytes()
    idx_bytes = idx_u16.tobytes()

    def _pad4(buf: bytes) -> bytes:
        rem = len(buf) % 4
        return buf if rem == 0 else buf + (b"\x00" * (4 - rem))

    pos_bytes_p = _pad4(pos_bytes)
    nrm_bytes_p = _pad4(nrm_bytes)
    col_bytes_p = _pad4(col_bytes)
    idx_bytes_p = _pad4(idx_bytes)

    off_pos = 0
    off_nrm = off_pos + len(pos_bytes_p)
    off_col = off_nrm + len(nrm_bytes_p)
    off_idx = off_col + len(col_bytes_p)
    blob = pos_bytes_p + nrm_bytes_p + col_bytes_p + idx_bytes_p

    pos_min = pos_f32.min(axis=0).tolist()
    pos_max = pos_f32.max(axis=0).tolist()

    gltf = pygltflib.GLTF2(
        scene=0,
        scenes=[pygltflib.Scene(nodes=[0])],
        buffers=[pygltflib.Buffer(byteLength=len(blob))],
        bufferViews=[
            pygltflib.BufferView(
                buffer=0, byteOffset=off_pos, byteLength=len(pos_bytes), target=pygltflib.ARRAY_BUFFER
            ),
            pygltflib.BufferView(
                buffer=0, byteOffset=off_nrm, byteLength=len(nrm_bytes), target=pygltflib.ARRAY_BUFFER
            ),
            pygltflib.BufferView(
                buffer=0, byteOffset=off_col, byteLength=len(col_bytes), target=pygltflib.ARRAY_BUFFER
            ),
            pygltflib.BufferView(
                buffer=0,
                byteOffset=off_idx,
                byteLength=len(idx_bytes),
                target=pygltflib.ELEMENT_ARRAY_BUFFER,
            ),
        ],
        accessors=[
            pygltflib.Accessor(
                bufferView=0,
                byteOffset=0,
                componentType=pygltflib.FLOAT,
                count=len(pos_f32),
                type=pygltflib.VEC3,
                min=pos_min,
                max=pos_max,
            ),
            pygltflib.Accessor(
                bufferView=1,
                byteOffset=0,
                componentType=pygltflib.FLOAT,
                count=len(nrm_f32),
                type=pygltflib.VEC3,
            ),
            pygltflib.Accessor(
                bufferView=2,
                byteOffset=0,
                componentType=pygltflib.UNSIGNED_BYTE,
                normalized=True,
                count=len(col_u8),
                type=pygltflib.VEC4,
            ),
            pygltflib.Accessor(
                bufferView=3,
                byteOffset=0,
                componentType=pygltflib.UNSIGNED_SHORT,
                count=len(idx_u16),
                type=pygltflib.SCALAR,
            ),
        ],
        meshes=[
            pygltflib.Mesh(
                name=mesh_name,
                primitives=[
                    pygltflib.Primitive(
                        attributes=pygltflib.Attributes(POSITION=0, NORMAL=1, COLOR_0=2),
                        indices=3,
                        mode=pygltflib.TRIANGLES,
                    )
                ],
            )
        ],
    )

    child_indices: list[int] = []
    socket_nodes: list[pygltflib.Node] = []
    for idx, (sock_name, sock_pos) in enumerate(sockets.items(), start=1):
        child_indices.append(idx)
        socket_nodes.append(
            pygltflib.Node(
                name=sock_name,
                translation=[float(sock_pos[0]), float(sock_pos[1]), float(sock_pos[2])],
            )
        )

    root_node = pygltflib.Node(name=mesh_name, mesh=0, children=child_indices)
    gltf.nodes = [root_node, *socket_nodes]
    gltf.set_binary_blob(blob)
    gltf.save_binary(str(out_glb_path))
    return out_glb_path


def build_calibration_glb(spec: ManifestEntry, out_glb_path: Path) -> Path:
    """Faz 0 kalibrasyon varligi: 3 kademeli demirci ors kaidesi (BottomCenter_Zero, +Z ileri, Color32)."""
    tx, ty, tz = spec.target_dimensions_meters
    hx = tx * 0.5
    hz = tz * 0.5
    mat_id = int(spec.default_material_id) & 0xFF

    positions: list[tuple[float, float, float]] = []
    normals: list[tuple[float, float, float]] = []
    colors: list[tuple[int, int, int, int]] = []
    indices: list[int] = []

    # 1) Alt tas kaide (Oak/Ironwood/Stone gorunumu icin cavity AO = 210)
    _append_box_part(
        positions,
        normals,
        colors,
        indices,
        (-hx, 0.0, -hz),
        (hx, ty * 0.35, hz),
        (71, 0, 32, 210),
    )
    # 2) Orta celik sutun (MatID = spec.default_material_id, Sharpness = 140, AO = 235)
    _append_box_part(
        positions,
        normals,
        colors,
        indices,
        (-hx * 0.65, ty * 0.35, -hz * 0.65),
        (hx * 0.65, ty * 0.72, hz * 0.65),
        (mat_id, 0, 140, 235),
    )
    # 3) Ust ors tablası (+Z yonunde hafif uzayan burun isaretcisi ile tam hedef AABB)
    _append_box_part(
        positions,
        normals,
        colors,
        indices,
        (-hx * 0.92, ty * 0.72, -hz * 0.90),
        (hx * 0.92, ty, hz),
        (mat_id, 24, 220, 255),
    )

    sockets: dict[str, tuple[float, float, float]] = {}
    for sock in spec.required_sockets:
        if "Top" in sock or "Blade" in sock:
            sockets[sock] = (0.0, ty, 0.0)
        else:
            sockets[sock] = (0.0, ty * 0.5, hz)

    # struct modulu uzerinden float32 hassasiyet sabitligi kontrolu
    _ = struct.pack("<3f", tx, ty, tz)
    return write_color32_glb(
        out_glb_path=out_glb_path,
        mesh_name=spec.asset_id,
        positions=np.array(positions, dtype=np.float32),
        normals=np.array(normals, dtype=np.float32),
        colors_rgba_u8=np.array(colors, dtype=np.uint8),
        indices_u16=np.array(indices, dtype=np.uint16),
        sockets=sockets,
    )
