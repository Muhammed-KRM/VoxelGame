"""Stratocraft Otonom 3B Fabrika - Moduler Silah Kabzasi, Balcak, Saft ve Menzilli Silah Ureticisi (Faz 2)."""

from __future__ import annotations

from pathlib import Path

import numpy as np

from .base_generator import (
    BaseAssetGenerator,
    ManifestEntry,
    _append_box_part,
    write_color32_glb,
)


def build_modular_weapon_hilt(spec: ManifestEntry, out_glb: Path) -> Path:
    """Kabza, pommel ve balcagi (Crossguard) uretir; ust birlesim merkezine Socket_VoxelBlade ekler."""
    tx, ty, tz = spec.target_dimensions_meters
    hx = tx * 0.5
    hz = tz * 0.5
    mat_id = int(spec.default_material_id) & 0xFF

    positions: list[tuple[float, float, float]] = []
    normals: list[tuple[float, float, float]] = []
    colors: list[tuple[int, int, int, int]] = []
    indices: list[int] = []

    # 1) Pommel (Alt topuz - metal/agirlik dengeleyici)
    _append_box_part(
        positions,
        normals,
        colors,
        indices,
        (-hx * 0.45, 0.0, -hz * 0.90),
        (hx * 0.45, ty * 0.18, hz * 0.90),
        (51, 0, 180, 220),
    )
    # 2) Ana Kabza (Grip - Ahsap/Deri sargili ana govde)
    _append_box_part(
        positions,
        normals,
        colors,
        indices,
        (-hx * 0.30, ty * 0.18, -hz * 0.70),
        (hx * 0.30, ty * 0.82, hz * 0.70),
        (mat_id, 0, 64, 235),
    )
    # 3) Balcak (Crossguard - tam hedef X/Z genisligini saglar, el dograma korumasi)
    _append_box_part(
        positions,
        normals,
        colors,
        indices,
        (-hx, ty * 0.82, -hz),
        (hx, ty, hz),
        (51, 0, 210, 250),
    )

    sockets: dict[str, tuple[float, float, float]] = {
        "Socket_Grip_Main": (0.0, ty * 0.55, 0.0),
        "Socket_Grip_Offhand": (0.0, ty * 0.28, 0.0),
        "Socket_VoxelBlade": (0.0, ty, 0.0),
    }
    for req in spec.required_sockets:
        if req not in sockets:
            sockets[req] = (0.0, ty * 0.85, hz)

    return write_color32_glb(
        out_glb_path=out_glb,
        mesh_name=spec.asset_id,
        positions=np.array(positions, dtype=np.float32),
        normals=np.array(normals, dtype=np.float32),
        colors_rgba_u8=np.array(colors, dtype=np.uint8),
        indices_u16=np.array(indices, dtype=np.uint16),
        sockets=sockets,
    )


class WeaponHiltGenerator(BaseAssetGenerator):
    """Moduler silah kabzalari, mizrak saftlari ve yay/arbalet govdeleri icin uretici sinif."""

    def generate(self, spec: ManifestEntry, out_glb_path: Path) -> Path:
        """ManifestEntry spesifikasyonuna uygun moduler silah .glb dosyasini uretir."""
        return build_modular_weapon_hilt(spec, out_glb_path)
