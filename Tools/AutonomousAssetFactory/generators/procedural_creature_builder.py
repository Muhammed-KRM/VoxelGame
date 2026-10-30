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

    asset_key = spec.asset_id.lower()
    if "phoenix" in asset_key or "boss" in asset_key:
        # Anka Kusu (Phoenix Ember Avatar): Genis katmanli tuy kanatlar, gagali bas ve kor gogus cekirdegi
        # Sol ve Sag Pence Bacaklar (y = 0.0 .. 0.38*ty)
        _append_box_part(positions, normals, colors, indices,
                         (-hx * 0.28, 0.0, -hz * 0.18), (-hx * 0.08, ty * 0.40, hz * 0.18),
                         (mat_id, 90, 140, 215), profile="sculpted")
        _append_box_part(positions, normals, colors, indices,
                         (hx * 0.08, 0.0, -hz * 0.18), (hx * 0.28, ty * 0.40, hz * 0.18),
                         (mat_id, 90, 140, 215), profile="sculpted")
        # Karin ve Alevli Gogus Cekirdegi (Kor Magma Parlamasi)
        _append_box_part(positions, normals, colors, indices,
                         (-hx * 0.32, ty * 0.34, -hz * 0.55), (hx * 0.32, ty * 0.78, hz * 0.62),
                         (mat_id, 220, 210, 250), profile="sculpted")
        # 3 Katmanli Acik Kanatlar (Tam X genisligi -hx .. +hx)
        _append_box_part(positions, normals, colors, indices,
                         (-hx, ty * 0.48, -hz * 0.36), (-hx * 0.24, ty * 0.82, hz * 0.32),
                         (mat_id, 180, 195, 240), profile="wing")
        _append_box_part(positions, normals, colors, indices,
                         (hx * 0.24, ty * 0.48, -hz * 0.36), (hx, ty * 0.82, hz * 0.32),
                         (mat_id, 180, 195, 240), profile="wing")
        # Kanat ucu ikincil telek katmanlari
        _append_box_part(positions, normals, colors, indices,
                         (-hx * 0.88, ty * 0.38, -hz * 0.48), (-hx * 0.30, ty * 0.58, -hz * 0.05),
                         (mat_id, 240, 230, 245), profile="wing")
        _append_box_part(positions, normals, colors, indices,
                         (hx * 0.30, ty * 0.38, -hz * 0.48), (hx * 0.88, ty * 0.58, -hz * 0.05),
                         (mat_id, 240, 230, 245), profile="wing")
        # Uzun Alev Kuyruk Yelpazesi (Tam -hz siniri)
        _append_box_part(positions, normals, colors, indices,
                         (-hx * 0.26, ty * 0.22, -hz), (hx * 0.26, ty * 0.52, -hz * 0.35),
                         (mat_id, 210, 200, 235), profile="horn")
        # Boyun, Tacli Bas ve Sivri Gaga (Tam ty ve +hz siniri)
        _append_box_part(positions, normals, colors, indices,
                         (-hx * 0.20, ty * 0.72, -hz * 0.15), (hx * 0.20, ty, hz * 0.55),
                         (mat_id, 235, 225, 255), profile="crystal")
        _append_box_part(positions, normals, colors, indices,
                         (-hx * 0.12, ty * 0.76, hz * 0.35), (hx * 0.12, ty * 0.92, hz),
                         (mat_id, 250, 245, 255), profile="horn")
    else:
        # Dort Ayakli Yirtici (Ash Stalker / Volkanik Kurt-Panter):
        # 4 eklemli pence bacak, kasli gogus kafesi, sirt obsidyen dikenleri, sivri kurt burnu ve kuyruk
        # On Sol ve Sag Bacaklar
        _append_box_part(positions, normals, colors, indices,
                         (-hx * 0.88, 0.0, hz * 0.22), (-hx * 0.32, ty * 0.52, hz * 0.62),
                         (mat_id, 25, 95, 210), profile="sculpted")
        _append_box_part(positions, normals, colors, indices,
                         (hx * 0.32, 0.0, hz * 0.22), (hx * 0.88, ty * 0.52, hz * 0.62),
                         (mat_id, 25, 95, 210), profile="sculpted")
        # Arka Sol ve Sag Bacaklar
        _append_box_part(positions, normals, colors, indices,
                         (-hx * 0.92, 0.0, -hz * 0.68), (-hx * 0.34, ty * 0.55, -hz * 0.22),
                         (mat_id, 20, 90, 205), profile="sculpted")
        _append_box_part(positions, normals, colors, indices,
                         (hx * 0.34, 0.0, -hz * 0.68), (hx * 0.92, ty * 0.55, -hz * 0.22),
                         (mat_id, 20, 90, 205), profile="sculpted")
        # Kasli Gogus Kafesi ve Karin Omurgasi (-hx..+hx zarfini korur)
        _append_box_part(positions, normals, colors, indices,
                         (-hx, ty * 0.40, -hz * 0.68), (hx, ty * 0.82, hz * 0.65),
                         (mat_id, 45, 130, 235), profile="sculpted")
        # Sirt Boyunca Obsidyen Kristal Dikenler (Dorsal Spines)
        _append_box_part(positions, normals, colors, indices,
                         (-hx * 0.28, ty * 0.72, -hz * 0.45), (hx * 0.28, ty * 0.96, hz * 0.25),
                         (mat_id, 140, 210, 250), profile="crystal")
        # Kurt Kafatasi, Dik Kulaklar ve Sivri Ceneli Burun (Tam ty ve +hz siniri)
        _append_box_part(positions, normals, colors, indices,
                         (-hx * 0.58, ty * 0.62, hz * 0.42), (hx * 0.58, ty, hz * 0.82),
                         (mat_id, 85, 165, 245), profile="sculpted")
        _append_box_part(positions, normals, colors, indices,
                         (-hx * 0.36, ty * 0.58, hz * 0.65), (hx * 0.36, ty * 0.84, hz),
                         (mat_id, 110, 195, 255), profile="horn")
        # Arkaya Uzanan Kamci Kuyruk (Tam -hz siniri)
        _append_box_part(positions, normals, colors, indices,
                         (-hx * 0.24, ty * 0.44, -hz), (hx * 0.24, ty * 0.72, -hz * 0.58),
                         (mat_id, 35, 115, 220), profile="horn")

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
