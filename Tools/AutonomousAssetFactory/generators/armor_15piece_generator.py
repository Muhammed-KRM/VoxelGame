"""Stratocraft Otonom 3B Fabrika - 15 Parca Zirh ve 64x64 Stop-Rib Haritasi Ureticisi (Faz 2)."""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image

from .base_generator import (
    BaseAssetGenerator,
    ManifestEntry,
    _append_box_part,
    write_color32_glb,
)

ARMOR_15_SLOTS: tuple[str, ...] = (
    "head_layer1_padded",
    "head_layer2_mail",
    "head_layer3_plate",
    "torso_layer1_gambeson",
    "torso_layer2_hauberk",
    "torso_layer3_cuirass",
    "arms_layer1_sleeve",
    "arms_layer2_vambrace_mail",
    "arms_layer3_pauldron_plate",
    "hands_layer1_liner",
    "hands_layer2_mail_glove",
    "hands_layer3_gauntlet",
    "legs_layer1_chausses_cloth",
    "legs_layer2_chausses_mail",
    "legs_layer3_greaves_plate",
)


def bake_armor_gap_map_64x64(out_png_path: Path) -> Path:
    """Koltuk alti, boyun ve diz arkasi eklem acikligi + Stop-Rib yakalama haritasini (64x64 RGBA) uretir."""
    out_png_path.parent.mkdir(parents=True, exist_ok=True)
    grid = np.zeros((64, 64, 4), dtype=np.uint8)

    # R = Plaka Kalinligi, G = Stop-Rib (Yakalayici Cikinti), B = Eklem Boslugu (GapMask), A = 255
    grid[:, :, 0] = 220  # Varsayilan plaka korumasi
    grid[:, :, 3] = 255

    # Boyun (y: 8..14) ve koltuk alti / kasik eklem bosluklari (B kanali yuksek)
    grid[8:14, 20:44, 0] = 40
    grid[8:14, 20:44, 2] = 240
    grid[22:34, 6:16, 0] = 55
    grid[22:34, 6:16, 2] = 230
    grid[22:34, 48:58, 0] = 55
    grid[22:34, 48:58, 2] = 230

    # Stop-Rib (Gogus ustu V-cikintisi ve omuz siperleri - G kanali = 255 -> bicak kaymasini durdurur)
    grid[15:19, 18:46, 1] = 255
    grid[18:24, 14:20, 1] = 240
    grid[18:24, 44:50, 1] = 240

    img = Image.fromarray(grid, mode="RGBA")
    img.save(out_png_path, format="PNG", optimize=True)
    return out_png_path


def build_15piece_armor_mesh(spec: ManifestEntry, out_glb: Path) -> Path:
    """5 Bolge alt-kutu geometrisini NormalPush ofsetiyle tek .glb icinde uretir."""
    tx, ty, tz = spec.target_dimensions_meters
    hx = tx * 0.5
    hz = tz * 0.5
    mat_id = int(spec.default_material_id) & 0xFF

    # Katmana gore keskinlik/parlaklik secimi (Layer 1 kumasta mat, Layer 3 plakada parlak)
    sharpness = 215 if "layer3" in spec.asset_id else (140 if "layer2" in spec.asset_id else 48)

    positions: list[tuple[float, float, float]] = []
    normals: list[tuple[float, float, float]] = []
    colors: list[tuple[int, int, int, int]] = []
    indices: list[int] = []

    # 1) Bacak zirh bolgesi (Greaves/Chausses: y = 0.0 .. 0.42*ty)
    _append_box_part(
        positions,
        normals,
        colors,
        indices,
        (-hx * 0.65, 0.0, -hz * 0.70),
        (hx * 0.65, ty * 0.42, hz * 0.70),
        (mat_id, 0, sharpness, 220),
    )
    # 2) Govde zirh bolgesi (Cuirass/Gambeson/Hauberk: y = 0.42*ty .. 0.78*ty)
    _append_box_part(
        positions,
        normals,
        colors,
        indices,
        (-hx * 0.80, ty * 0.42, -hz),
        (hx * 0.80, ty * 0.78, hz),
        (mat_id, 0, sharpness, 245),
    )
    # 3) Kol ve El zirh bolgesi (Pauldrons/Vambraces/Gauntlets: tam X genisligi -hx .. +hx)
    _append_box_part(
        positions,
        normals,
        colors,
        indices,
        (-hx, ty * 0.46, -hz * 0.65),
        (hx, ty * 0.76, hz * 0.65),
        (mat_id, 0, sharpness, 235),
    )
    # 4) Bas/Migfer bolgesi (Helmet/Coif: y = 0.78*ty .. ty)
    _append_box_part(
        positions,
        normals,
        colors,
        indices,
        (-hx * 0.48, ty * 0.78, -hz * 0.75),
        (hx * 0.48, ty, hz * 0.75),
        (mat_id, 0, sharpness, 255),
    )

    sockets: dict[str, tuple[float, float, float]] = {
        "Socket_Zone_Head": (0.0, ty * 0.90, 0.0),
        "Socket_Zone_Torso": (0.0, ty * 0.60, 0.0),
        "Socket_Zone_Arms": (hx * 0.85, ty * 0.62, 0.0),
        "Socket_Zone_Hands": (hx * 0.95, ty * 0.48, 0.0),
        "Socket_Zone_Legs": (0.0, ty * 0.22, 0.0),
    }
    for req in spec.required_sockets:
        if req not in sockets:
            sockets[req] = (0.0, ty * 0.60, hz)

    # Eger zirh klasoru altina uretiliyorsa 64x64 Stop-Rib / GapMap atlasini da garantili uret
    gap_map_path = out_glb.parent / "T_ArmorGapMap_64x64_Atlas.png"
    if not gap_map_path.exists():
        bake_armor_gap_map_64x64(gap_map_path)

    return write_color32_glb(
        out_glb_path=out_glb,
        mesh_name=spec.asset_id,
        positions=np.array(positions, dtype=np.float32),
        normals=np.array(normals, dtype=np.float32),
        colors_rgba_u8=np.array(colors, dtype=np.uint8),
        indices_u16=np.array(indices, dtype=np.uint16),
        sockets=sockets,
    )


def build_15piece_armor_set(set_id: str, material_id: int, out_dir: Path) -> list[Path]:
    """Belirtilen zirh seti icin 3 katman .glb dosyasini ve 64x64 GapMap dokusunu uretir."""
    out_dir.mkdir(parents=True, exist_ok=True)
    bake_armor_gap_map_64x64(out_dir / "T_ArmorGapMap_64x64_Atlas.png")
    generated: list[Path] = []
    for layer_idx in (1, 2, 3):
        push = 0.01 * layer_idx
        spec = ManifestEntry(
            asset_id=f"{set_id}_layer{layer_idx}",
            category="Armor15Piece",
            phase=2,
            generator_path="PathA_BlenderBpy",
            target_dimensions_meters=(0.62 + push, 1.76 + push, 0.36 + push),
            dimension_tolerance_ratio=0.02,
            pivot_rule="BottomCenter_Zero",
            forward_axis="+Z",
            up_axis="+Y",
            poly_budget_lod0=600,
            poly_budget_lod1=300,
            poly_budget_lod2=120,
            default_material_id=material_id,
            required_sockets=["Socket_Zone_Head", "Socket_Zone_Torso", "Socket_Zone_Legs"],
        )
        out_file = out_dir / f"{spec.asset_id}.glb"
        generated.append(build_15piece_armor_mesh(spec, out_file))
    return generated


class Armor15PieceGenerator(BaseAssetGenerator):
    """5 Bolge x 3 Katman = 15 parca zirh uretici sinif."""

    def generate(self, spec: ManifestEntry, out_glb_path: Path) -> Path:
        """Zirh .glb modelini ve 64x64 GapMap dokusunu uretir."""
        return build_15piece_armor_mesh(spec, out_glb_path)
