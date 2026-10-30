"""Stratocraft Otonom 3B Fabrika - Yol A Prosedurel 21 Yaratik ve 13 Boss Ureticisi (Faz 2)."""

from __future__ import annotations

from pathlib import Path

import numpy as np

from .base_generator import (
    BaseAssetGenerator,
    ManifestEntry,
    _append_box_part,
    write_color32_glb,
)


def generate_creature_or_boss(
    spec: ManifestEntry, out_glb: Path, use_neural_path_b: bool = False
) -> Path:
    """21 Yaratik, 13 Rituel Boss'u ve Karakter modellerini Socket_GoreCap_* ile uretir."""
    _ = use_neural_path_b  # Yol A CPU deterministik uretim iki bilgisayarda da %100 ayni ciktiyi verir
    tx, ty, tz = spec.target_dimensions_meters
    hx = tx * 0.5
    hz = tz * 0.5
    mat_id = int(spec.default_material_id) & 0xFF

    positions: list[tuple[float, float, float]] = []
    normals: list[tuple[float, float, float]] = []
    colors: list[tuple[int, int, int, int]] = []
    indices: list[int] = []

    # 1) Sol ve Sag Bacaklar (y = 0.0 .. 0.45*ty)
    _append_box_part(
        positions,
        normals,
        colors,
        indices,
        (-hx * 0.65, 0.0, -hz * 0.35),
        (-hx * 0.15, ty * 0.45, hz * 0.35),
        (mat_id, 0, 80, 210),
    )
    _append_box_part(
        positions,
        normals,
        colors,
        indices,
        (hx * 0.15, 0.0, -hz * 0.35),
        (hx * 0.65, ty * 0.45, hz * 0.35),
        (mat_id, 0, 80, 210),
    )
    # 2) Ana Govde / Torso (y = 0.42*ty .. 0.80*ty, tam Z derinligi -hz .. +hz)
    _append_box_part(
        positions,
        normals,
        colors,
        indices,
        (-hx * 0.72, ty * 0.42, -hz),
        (hx * 0.72, ty * 0.80, hz),
        (mat_id, 16, 120, 240),
    )
    # 3) Kollar / Kanatlar (tam X genisligi -hx .. +hx)
    _append_box_part(
        positions,
        normals,
        colors,
        indices,
        (-hx, ty * 0.48, -hz * 0.45),
        (hx, ty * 0.76, hz * 0.45),
        (mat_id, 0, 110, 230),
    )
    # 4) Bas / Boyun / Zayif Nokta Tepesi (y = 0.80*ty .. ty)
    _append_box_part(
        positions,
        normals,
        colors,
        indices,
        (-hx * 0.42, ty * 0.80, -hz * 0.55),
        (hx * 0.42, ty, hz * 0.65),
        (mat_id, 64, 180, 255),
    )

    sockets: dict[str, tuple[float, float, float]] = {
        "Socket_GoreCap_Head": (0.0, ty * 0.80, 0.0),
        "Socket_GoreCap_L_ForeArm": (-hx * 0.85, ty * 0.60, 0.0),
        "Socket_GoreCap_R_ForeArm": (hx * 0.85, ty * 0.60, 0.0),
        "Socket_GoreCap_L_Calf": (-hx * 0.40, ty * 0.22, 0.0),
        "Socket_GoreCap_R_Calf": (hx * 0.40, ty * 0.22, 0.0),
        "Socket_WeakPoint_Head": (0.0, ty * 0.90, hz * 0.55),
        "Socket_WeakPoint_ChestCore": (0.0, ty * 0.62, hz * 0.95),
    }
    for bone in spec.dismemberable_bones:
        cap_name = f"Socket_GoreCap_{bone}"
        if cap_name not in sockets:
            sockets[cap_name] = (0.0, ty * 0.70, 0.0)
    for req in spec.required_sockets:
        if req not in sockets:
            sockets[req] = (0.0, ty * 0.50, hz * 0.50)

    return write_color32_glb(
        out_glb_path=out_glb,
        mesh_name=spec.asset_id,
        positions=np.array(positions, dtype=np.float32),
        normals=np.array(normals, dtype=np.float32),
        colors_rgba_u8=np.array(colors, dtype=np.uint8),
        indices_u16=np.array(indices, dtype=np.uint16),
        sockets=sockets,
    )


class ProceduralCreatureBuilder(BaseAssetGenerator):
    """Yol A prosedurel yaratik, oyuncu iskeleti ve Boss uretici sinifi."""

    def generate(self, spec: ManifestEntry, out_glb_path: Path) -> Path:
        """Yaratik veya Boss .glb modelini uretir."""
        return generate_creature_or_boss(spec, out_glb_path, use_neural_path_b=False)
