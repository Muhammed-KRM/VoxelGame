"""Stratocraft Otonom 3B Fabrika - 6 Atolye, 24 Aktif Yapi ve Kusatma DFA Ureticisi (Faz 2)."""

from __future__ import annotations

from pathlib import Path

import numpy as np

from .base_generator import (
    BaseAssetGenerator,
    ManifestEntry,
    _append_box_part,
    write_color32_glb,
)


def build_workshop_or_clan_structure(spec: ManifestEntry, out_glb: Path) -> Path:
    """Atolye cihazlari, Klan Kristali ve aktif klan/sosyal yapilar icin soketli Color32 .glb uretir."""
    tx, ty, tz = spec.target_dimensions_meters
    hx = tx * 0.5
    hz = tz * 0.5
    mat_id = int(spec.default_material_id) & 0xFF

    positions: list[tuple[float, float, float]] = []
    normals: list[tuple[float, float, float]] = []
    colors: list[tuple[int, int, int, int]] = []
    indices: list[int] = []

    # 1) Agir tas/metal taban platformu
    _append_box_part(
        positions,
        normals,
        colors,
        indices,
        (-hx, 0.0, -hz),
        (hx, ty * 0.25, hz),
        (1, 0, 48, 200),
    )
    # 2) Orta govde / tezgah sutunu
    _append_box_part(
        positions,
        normals,
        colors,
        indices,
        (-hx * 0.82, ty * 0.25, -hz * 0.82),
        (hx * 0.82, ty * 0.78, hz * 0.82),
        (mat_id, 0, 140, 235),
    )
    # 3) Ust kristal / tezgah calisma kulesi
    _append_box_part(
        positions,
        normals,
        colors,
        indices,
        (-hx * 0.55, ty * 0.78, -hz * 0.55),
        (hx * 0.55, ty, hz * 0.55),
        (mat_id, 80, 230, 255),
    )

    sockets: dict[str, tuple[float, float, float]] = {
        "Socket_Interact": (0.0, ty * 0.50, hz),
        "Socket_InputSlot": (-hx * 0.45, ty * 0.78, 0.0),
        "Socket_OutputSlot": (hx * 0.45, ty * 0.78, 0.0),
    }
    for req in spec.required_sockets:
        if req not in sockets:
            sockets[req] = (0.0, ty * 0.85, 0.0)

    return write_color32_glb(
        out_glb_path=out_glb,
        mesh_name=spec.asset_id,
        positions=np.array(positions, dtype=np.float32),
        normals=np.array(normals, dtype=np.float32),
        colors_rgba_u8=np.array(colors, dtype=np.uint8),
        indices_u16=np.array(indices, dtype=np.uint16),
        sockets=sockets,
    )


def build_siege_weapon_5stage_dfa(spec: ManifestEntry, out_glb: Path) -> Path:
    """Stage_1_Wheels .. Stage_5_Ammo kademelerini ve Socket_Crew_Gunner/Pusher_1..2 dugumlerini uretir."""
    tx, ty, tz = spec.target_dimensions_meters
    hx = tx * 0.5
    hz = tz * 0.5
    mat_id = int(spec.default_material_id) & 0xFF

    positions: list[tuple[float, float, float]] = []
    normals: list[tuple[float, float, float]] = []
    colors: list[tuple[int, int, int, int]] = []
    indices: list[int] = []

    # Stage 1: Wheels & Axles (Alt tekerlek dingili)
    _append_box_part(
        positions,
        normals,
        colors,
        indices,
        (-hx, 0.0, -hz * 0.85),
        (hx, ty * 0.25, hz * 0.85),
        (70, 0, 64, 200),
    )
    # Stage 2: Heavy Chassis (Agir sasi)
    _append_box_part(
        positions,
        normals,
        colors,
        indices,
        (-hx * 0.85, ty * 0.25, -hz),
        (hx * 0.85, ty * 0.50, hz),
        (mat_id, 0, 96, 220),
    )
    # Stage 3..5: Main Body, Torsion Arms & Barrel/Projectile
    _append_box_part(
        positions,
        normals,
        colors,
        indices,
        (-hx * 0.60, ty * 0.50, -hz * 0.90),
        (hx * 0.60, ty, hz),
        (51, 0, 190, 250),
    )

    sockets: dict[str, tuple[float, float, float]] = {
        "Socket_Crew_Gunner": (0.0, ty * 0.45, -hz),
        "Socket_Crew_Pusher_1": (-hx, ty * 0.35, -hz * 0.5),
        "Socket_Crew_Pusher_2": (hx, ty * 0.35, -hz * 0.5),
        "Socket_Muzzle": (0.0, ty * 0.88, hz),
    }
    for req in spec.required_sockets:
        if req not in sockets:
            sockets[req] = (0.0, ty * 0.50, 0.0)

    return write_color32_glb(
        out_glb_path=out_glb,
        mesh_name=spec.asset_id,
        positions=np.array(positions, dtype=np.float32),
        normals=np.array(normals, dtype=np.float32),
        colors_rgba_u8=np.array(colors, dtype=np.uint8),
        indices_u16=np.array(indices, dtype=np.uint16),
        sockets=sockets,
    )


class WorkshopAndClanGenerator(BaseAssetGenerator):
    """Atolye, klan yapisi ve kusatma araclari uretici sinifi."""

    def generate(self, spec: ManifestEntry, out_glb_path: Path) -> Path:
        """Kategoriye gore atolye/klan veya 5 asamali kusatma araci .glb dosyasini uretir."""
        if spec.category == "SiegeWeapon":
            return build_siege_weapon_5stage_dfa(spec, out_glb_path)
        return build_workshop_or_clan_structure(spec, out_glb_path)
