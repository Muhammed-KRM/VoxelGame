"""Stratocraft Otonom 3B Varlik Fabrikasi - Soyut Uretici ve Deterministik GLB Insa Sinifi."""

from __future__ import annotations

import abc
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


def _append_box_part(
    positions: list[tuple[float, float, float]],
    normals: list[tuple[float, float, float]],
    colors: list[tuple[int, int, int, int]],
    indices: list[int],
    min_pt: tuple[float, float, float],
    max_pt: tuple[float, float, float],
    rgba: tuple[int, int, int, int],
) -> None:
    """Verilen eksen-hizali kutuyu (AABB) sert yuzey normalleri ve Color32 ile mesh listesine ekler."""
    x0, y0, z0 = min_pt
    x1, y1, z1 = max_pt
    faces = [
        # +Z (On)
        ((0.0, 0.0, 1.0), [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]),
        # -Z (Arka)
        ((0.0, 0.0, -1.0), [(x1, y0, z0), (x0, y0, z0), (x0, y1, z0), (x1, y1, z0)]),
        # +X (Sag)
        ((1.0, 0.0, 0.0), [(x1, y0, z1), (x1, y0, z0), (x1, y1, z0), (x1, y1, z1)]),
        # -X (Sol)
        ((-1.0, 0.0, 0.0), [(x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0)]),
        # +Y (Ust)
        ((0.0, 1.0, 0.0), [(x0, y1, z1), (x1, y1, z1), (x1, y1, z0), (x0, y1, z0)]),
        # -Y (Alt)
        ((0.0, -1.0, 0.0), [(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)]),
    ]
    for normal, quad in faces:
        base_idx = len(positions)
        for v in quad:
            positions.append(v)
            normals.append(normal)
            colors.append(rgba)
        indices.extend([base_idx, base_idx + 1, base_idx + 2, base_idx, base_idx + 2, base_idx + 3])


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
