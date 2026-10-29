"""Stratocraft Otonom 3B Varlik Kalite Kapisi (asset_validator.py - Bolum 4.8 & 4.13.2)."""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path

import numpy as np
import pygltflib

from generators.base_generator import ManifestEntry


@dataclass(slots=True)
class ValidationResult:
    """Tek bir .glb varliginin otomatik kalite kapisi denetim sonucu."""

    is_valid: bool
    asset_id: str
    measured_dimensions: tuple[float, float, float] = (0.0, 0.0, 0.0)
    measured_pivot_offset: tuple[float, float, float] = (0.0, 0.0, 0.0)
    triangle_count: int = 0
    has_color32: bool = False
    detected_material_ids: list[int] = field(default_factory=list)
    missing_sockets: list[str] = field(default_factory=list)
    errors: list[str] = field(default_factory=list)


def _read_accessor_array(gltf: pygltflib.GLTF2, blob: bytes, accessor_idx: int) -> np.ndarray:
    """GLTF accessor verisini numpy dizisi olarak cikarir."""
    acc = gltf.accessors[accessor_idx]
    bv = gltf.bufferViews[acc.bufferView]
    offset = (bv.byteOffset or 0) + (acc.byteOffset or 0)

    comp_map = {
        pygltflib.BYTE: np.int8,
        pygltflib.UNSIGNED_BYTE: np.uint8,
        pygltflib.SHORT: np.int16,
        pygltflib.UNSIGNED_SHORT: np.uint16,
        pygltflib.UNSIGNED_INT: np.uint32,
        pygltflib.FLOAT: np.float32,
    }
    type_counts = {
        pygltflib.SCALAR: 1,
        pygltflib.VEC2: 2,
        pygltflib.VEC3: 3,
        pygltflib.VEC4: 4,
    }
    dtype = comp_map[acc.componentType]
    num_comp = type_counts[acc.type]
    total_elems = acc.count * num_comp
    item_size = np.dtype(dtype).itemsize
    raw_slice = blob[offset : offset + total_elems * item_size]
    arr = np.frombuffer(raw_slice, dtype=dtype)
    return arr.reshape((acc.count, num_comp)) if num_comp > 1 else arr


def validate_glb_asset(
    glb_path: Path,
    spec: ManifestEntry,
    contact_sheet_png: Path | None = None,
) -> ValidationResult:
    """Bolum 4.8'deki 6 zorunlu kurala gore .glb varligini denetler."""
    errors: list[str] = []
    if not glb_path.exists():
        return ValidationResult(
            is_valid=False,
            asset_id=spec.asset_id,
            errors=[f"ASSET_FILE_MISSING: {glb_path}"],
        )

    try:
        gltf = pygltflib.GLTF2().load(str(glb_path))
        blob = gltf.binary_blob() or b""
    except Exception as exc:  # noqa: BLE001
        return ValidationResult(
            is_valid=False,
            asset_id=spec.asset_id,
            errors=[f"ASSET_GLB_CORRUPT: {exc}"],
        )

    if not gltf.meshes:
        return ValidationResult(
            is_valid=False,
            asset_id=spec.asset_id,
            errors=["ASSET_NO_MESH: En az 1 gecerli mesh bulunmalidir."],
        )

    all_positions: list[np.ndarray] = []
    total_triangles = 0
    has_color32 = True
    detected_mats: set[int] = set()

    for mesh in gltf.meshes:
        for prim in mesh.primitives:
            if prim.attributes.POSITION is None:
                errors.append("ASSET_NO_POSITION_ATTR")
                continue
            pos = _read_accessor_array(gltf, blob, prim.attributes.POSITION).astype(np.float32)
            all_positions.append(pos)

            if prim.indices is not None:
                idx = _read_accessor_array(gltf, blob, prim.indices)
                total_triangles += int(len(idx) // 3)
            else:
                total_triangles += int(len(pos) // 3)

            if prim.attributes.COLOR_0 is None:
                has_color32 = False
                errors.append("ASSET_MISSING_COLOR32: UV-Free UberShader icin COLOR_0 zorunludur.")
            else:
                col_acc = gltf.accessors[prim.attributes.COLOR_0]
                cols = _read_accessor_array(gltf, blob, prim.attributes.COLOR_0)
                if col_acc.componentType == pygltflib.FLOAT:
                    r_vals = np.clip(np.round(cols[:, 0] * 255.0), 0, 255).astype(np.uint8)
                else:
                    r_vals = cols[:, 0].astype(np.uint8)
                for m_id in np.unique(r_vals).tolist():
                    detected_mats.add(int(m_id))

    if not all_positions:
        return ValidationResult(
            is_valid=False,
            asset_id=spec.asset_id,
            errors=[*errors, "ASSET_EMPTY_VERTICES"],
        )

    merged_pos = np.vstack(all_positions)
    min_pt = merged_pos.min(axis=0)
    max_pt = merged_pos.max(axis=0)
    dims = tuple(float(v) for v in (max_pt - min_pt))

    # Kural 2: AABB metre boyutlari tolerans kontrolu (±%2)
    tol = max(spec.dimension_tolerance_ratio, 1e-4)
    for axis_name, actual, target in zip(("X", "Y", "Z"), dims, spec.target_dimensions_meters, strict=True):
        if target > 0.0:
            rel_err = abs(actual - target) / target
            if rel_err > tol + 1e-5:
                errors.append(
                    f"ASSET_AABB_OUT_OF_TOLERANCE_{axis_name}: Olculen={actual:.4f}m, "
                    f"Hedef={target:.4f}m, Sapma={rel_err * 100.0:.2f}% > {tol * 100.0:.1f}%"
                )

    # Pivot kurali: BottomCenter_Zero -> taban merkezi (0, 0, 0) civarinda olmali
    center_x = float((min_pt[0] + max_pt[0]) * 0.5)
    bottom_y = float(min_pt[1])
    center_z = float((min_pt[2] + max_pt[2]) * 0.5)
    pivot_offset = (center_x, bottom_y, center_z)
    if spec.pivot_rule == "BottomCenter_Zero":
        max_dim = max(spec.target_dimensions_meters)
        pivot_tol = max(0.01, max_dim * tol)
        if abs(center_x) > pivot_tol or abs(bottom_y) > pivot_tol or abs(center_z) > pivot_tol:
            errors.append(
                "ASSET_PIVOT_INVALID: BottomCenter_Zero sapmasi="
                f"({center_x:.4f}, {bottom_y:.4f}, {center_z:.4f})"
            )

    # Kural 3: Ucgen butcesi (LOD0)
    if total_triangles > spec.poly_budget_lod0:
        errors.append(
            f"ASSET_POLY_BUDGET_EXCEEDED: {total_triangles} tri > LOD0 butcesi ({spec.poly_budget_lod0})"
        )

    # Kural 4: Zorunlu Socket_* dugumleri
    existing_node_names = {n.name for n in (gltf.nodes or []) if n.name}
    missing_sockets = [s for s in spec.required_sockets if s not in existing_node_names]
    if missing_sockets:
        errors.append(f"ASSET_MISSING_SOCKETS: {', '.join(missing_sockets)}")

    # Kural 5: Gecerli MaterialID (0 = Air haric 1..255)
    if has_color32 and (not detected_mats or 0 in detected_mats):
        errors.append(
            "ASSET_INVALID_MATERIAL_ID: Color32.R icinde gecersiz MaterialID=0 (Air) tespit edildi."
        )

    # Kural 6: 4-acili kontrol gorseli istenmisse varligi kontrol edilir
    if contact_sheet_png is not None and not contact_sheet_png.exists():
        errors.append(f"ASSET_CONTACT_SHEET_MISSING: {contact_sheet_png}")

    return ValidationResult(
        is_valid=(len(errors) == 0),
        asset_id=spec.asset_id,
        measured_dimensions=dims,
        measured_pivot_offset=pivot_offset,
        triangle_count=total_triangles,
        has_color32=has_color32,
        detected_material_ids=sorted(detected_mats),
        missing_sockets=missing_sockets,
        errors=errors,
    )


def auto_correct_glb_transform(glb_path: Path, spec: ManifestEntry) -> None:
    """Olcek veya BottomCenter_Zero pivot sapmasi olan bir .glb dosyasini yerinde duzeltir."""
    gltf = pygltflib.GLTF2().load(str(glb_path))
    blob = bytearray(gltf.binary_blob() or b"")

    pos_accessors: list[int] = []
    all_pos: list[np.ndarray] = []
    for mesh in gltf.meshes:
        for prim in mesh.primitives:
            if prim.attributes.POSITION is not None:
                pos_accessors.append(prim.attributes.POSITION)
                all_pos.append(_read_accessor_array(gltf, bytes(blob), prim.attributes.POSITION).copy())

    if not all_pos:
        return

    merged = np.vstack(all_pos)
    min_pt = merged.min(axis=0)
    max_pt = merged.max(axis=0)
    cur_dims = np.maximum(max_pt - min_pt, 1e-6)
    target_dims = np.array(spec.target_dimensions_meters, dtype=np.float32)
    scale_vec = target_dims / cur_dims

    center_x = (min_pt[0] + max_pt[0]) * 0.5
    bottom_y = min_pt[1]
    center_z = (min_pt[2] + max_pt[2]) * 0.5
    shift_before_scale = np.array([-center_x, -bottom_y, -center_z], dtype=np.float32)

    for acc_idx in set(pos_accessors):
        acc = gltf.accessors[acc_idx]
        bv = gltf.bufferViews[acc.bufferView]
        offset = (bv.byteOffset or 0) + (acc.byteOffset or 0)
        pts = _read_accessor_array(gltf, bytes(blob), acc_idx).astype(np.float32).copy()
        pts = (pts + shift_before_scale) * scale_vec
        raw_new = pts.astype(np.float32).tobytes()
        blob[offset : offset + len(raw_new)] = raw_new
        acc.min = pts.min(axis=0).tolist()
        acc.max = pts.max(axis=0).tolist()

    # Eksik soket dugumleri varsa otomatik olarak kok dugume bagla
    existing_names = {n.name for n in (gltf.nodes or []) if n.name}
    if gltf.nodes:
        root = gltf.nodes[0]
        if root.children is None:
            root.children = []
        for sock in spec.required_sockets:
            if sock not in existing_names:
                new_idx = len(gltf.nodes)
                gltf.nodes.append(
                    pygltflib.Node(name=sock, translation=[0.0, float(target_dims[1]), 0.0])
                )
                root.children.append(new_idx)

    gltf.set_binary_blob(bytes(blob))
    gltf.save_binary(str(glb_path))
