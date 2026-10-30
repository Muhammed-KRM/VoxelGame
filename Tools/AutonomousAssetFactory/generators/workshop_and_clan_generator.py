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

    asset_key = spec.asset_id.lower()
    if "crystal" in asset_key or "totem" in asset_key:
        # Klan Kristali (Clan Protection Crystal & KGP Totem):
        # 1) Sekizgen Runik Tas Sunak Kaidesi (-hx..+hx, -hz..+hz zarfini korur)
        _append_box_part(positions, normals, colors, indices,
                         (-hx, 0.0, -hz), (hx, ty * 0.24, hz),
                         (1, 0, 95, 210), profile="sculpted")
        _append_box_part(positions, normals, colors, indices,
                         (-hx * 0.72, ty * 0.20, -hz * 0.72), (hx * 0.72, ty * 0.36, hz * 0.72),
                         (6, 40, 175, 235), profile="sculpted")
        # 2) Merkezde Havada Suzulen Ana Altigen/Sekizgen KGP Enerji Kristali (Tam ty zirvesi)
        _append_box_part(positions, normals, colors, indices,
                         (-hx * 0.44, ty * 0.34, -hz * 0.44), (hx * 0.44, ty, hz * 0.44),
                         (mat_id, 180, 245, 255), profile="crystal")
        # 3) Cevrede Donen 4 Yardimci Rezonans Kristal Parcasi
        _append_box_part(positions, normals, colors, indices,
                         (-hx * 0.86, ty * 0.42, -hz * 0.24), (-hx * 0.50, ty * 0.78, hz * 0.24),
                         (mat_id, 150, 230, 250), profile="crystal")
        _append_box_part(positions, normals, colors, indices,
                         (hx * 0.50, ty * 0.42, -hz * 0.24), (hx * 0.86, ty * 0.78, hz * 0.24),
                         (mat_id, 150, 230, 250), profile="crystal")
        _append_box_part(positions, normals, colors, indices,
                         (-hx * 0.24, ty * 0.44, -hz * 0.86), (hx * 0.24, ty * 0.80, -hz * 0.50),
                         (mat_id, 160, 235, 250), profile="crystal")
        _append_box_part(positions, normals, colors, indices,
                         (-hx * 0.24, ty * 0.44, hz * 0.50), (hx * 0.24, ty * 0.80, hz * 0.86),
                         (mat_id, 160, 235, 250), profile="crystal")
    else:
        # 16x32x4 Demirci Ors Atolyesi (Blacksmith Anvil & Forge Hearth):
        # 1) Agir Bazalt Atolye Taban Platformu (-hx..+hx, -hz..+hz)
        _append_box_part(positions, normals, colors, indices,
                         (-hx, 0.0, -hz), (hx, ty * 0.22, hz),
                         (1, 0, 75, 205), profile="sculpted")
        # 2) Demirci Ocak Haznesi ve Kor Ates Yatagi (Sol/Arka bolge)
        _append_box_part(positions, normals, colors, indices,
                         (-hx * 0.92, ty * 0.20, -hz * 0.88), (-hx * 0.15, ty, hz * 0.45),
                         (8, 120, 145, 230), profile="sculpted")
        _append_box_part(positions, normals, colors, indices,
                         (-hx * 0.75, ty * 0.48, -hz * 0.55), (-hx * 0.22, ty * 0.72, hz * 0.25),
                         (10, 250, 240, 255), profile="crystal")
        # 3) Kutuk Kaide ve Boynuzlu Celik Demirci Orsu (Horned Blacksmith Anvil - Sag/On bolge)
        _append_box_part(positions, normals, colors, indices,
                         (hx * 0.05, ty * 0.20, -hz * 0.45), (hx * 0.78, ty * 0.56, hz * 0.45),
                         (2, 15, 95, 215), profile="sculpted")
        _append_box_part(positions, normals, colors, indices,
                         (hx * 0.02, ty * 0.54, -hz * 0.68), (hx * 0.86, ty * 0.86, hz * 0.82),
                         (mat_id, 30, 210, 245), profile="horn")
        # 4) Ors Uzerinde Dovulen Akkor Kilic Namlusu (16x32x4 Voxel Preform)
        _append_box_part(positions, normals, colors, indices,
                         (hx * 0.22, ty * 0.85, -hz * 0.52), (hx * 0.62, ty * 0.95, hz * 0.65),
                         (6, 245, 250, 255), profile="wing")

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

    # Stage 1: 4 Adet Sekizgen Kusatma Tekerlegi ve Demir Dingiller (-hx..+hx)
    _append_box_part(positions, normals, colors, indices,
                     (-hx, 0.0, hz * 0.35), (-hx * 0.72, ty * 0.30, hz * 0.85),
                     (4, 0, 120, 210), profile="wheel")
    _append_box_part(positions, normals, colors, indices,
                     (hx * 0.72, 0.0, hz * 0.35), (hx, ty * 0.30, hz * 0.85),
                     (4, 0, 120, 210), profile="wheel")
    _append_box_part(positions, normals, colors, indices,
                     (-hx, 0.0, -hz * 0.85), (-hx * 0.72, ty * 0.30, -hz * 0.35),
                     (4, 0, 120, 210), profile="wheel")
    _append_box_part(positions, normals, colors, indices,
                     (hx * 0.72, 0.0, -hz * 0.85), (hx, ty * 0.30, -hz * 0.35),
                     (4, 0, 120, 210), profile="wheel")
    # Stage 2: Agir Ahsap Sasi Kirisleri (-hz..+hz)
    _append_box_part(positions, normals, colors, indices,
                     (-hx * 0.78, ty * 0.14, -hz), (hx * 0.78, ty * 0.34, hz),
                     (mat_id, 0, 105, 220), profile="sculpted")
    # Stage 3: Sol ve Sag A-Cati Kule Dikmeleri (Trestle Frame)
    _append_box_part(positions, normals, colors, indices,
                     (-hx * 0.68, ty * 0.30, -hz * 0.48), (-hx * 0.36, ty * 0.86, hz * 0.48),
                     (mat_id, 10, 130, 235), profile="crystal")
    _append_box_part(positions, normals, colors, indices,
                     (hx * 0.36, ty * 0.30, -hz * 0.48), (hx * 0.68, ty * 0.86, hz * 0.48),
                     (mat_id, 10, 130, 235), profile="crystal")
    # Stage 4: Uzun Mancinik Firlatma Kolu (Throwing Beam - Tam ty ve +hz siniri)
    _append_box_part(positions, normals, colors, indices,
                     (-hx * 0.20, ty * 0.56, -hz * 0.92), (hx * 0.20, ty, hz),
                     (mat_id, 25, 165, 245), profile="horn")
    # Stage 5: Sekizgen Agir Tas Karsi-Agirlik Sandigi (Counterweight Box)
    _append_box_part(positions, normals, colors, indices,
                     (-hx * 0.32, ty * 0.36, hz * 0.32), (hx * 0.32, ty * 0.74, hz * 0.86),
                     (1, 0, 195, 250), profile="crystal")

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
