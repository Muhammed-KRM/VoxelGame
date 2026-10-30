"""Stratocraft 15 Parca Zirh Otomatik Agirlik Aktarimi ve Gap-Map Atlasi (Bolum 4.6 & 5.3)."""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image

from generators.armor_15piece_generator import ARMOR_15_SLOTS, Armor15PieceGenerator
from generators.base_generator import ManifestEntry


def bake_armor_gap_map_atlas(out_png_path: Path, resolution: int = 64) -> Path:
    """15 parca 3 katmanli zirh sistemi icin 64x64 RGBA Gap-Map / Katman Maskesi uretir."""
    out_png_path.parent.mkdir(parents=True, exist_ok=True)
    res = max(16, int(resolution))
    atlas = np.zeros((res, res, 4), dtype=np.uint8)

    # 4x4 hucreli 64x64 grid: Ilk 15 hucre 15 zirh yuvasini, 16. hucre eklem bosluklarini temsil eder
    cell_size = res // 4
    for slot_idx in range(16):
        row = slot_idx // 4
        col = slot_idx % 4
        y0, y1 = row * cell_size, (row + 1) * cell_size
        x0, x1 = col * cell_size, (col + 1) * cell_size

        # R: Dis Plaka (Katman 3) kaplama orani, G: Orta Zincir (Katman 2),
        # B: Ic Deri/Kumas (Katman 1), A: Eklem aciklik (Gap) esigi
        plate_cov = max(90, 240 - slot_idx * 6)
        mail_cov = 215
        cloth_cov = 250
        gap_mask = 48 if slot_idx < 15 else 220

        atlas[y0:y1, x0:x1, 0] = plate_cov
        atlas[y0:y1, x0:x1, 1] = mail_cov
        atlas[y0:y1, x0:x1, 2] = cloth_cov
        atlas[y0:y1, x0:x1, 3] = gap_mask

        # Hucre sinirlarinda 1 piksellik eklem boslugu (Weak-spot / Gap)
        atlas[y0 : y0 + 1, x0:x1, 0] = 0
        atlas[y0:y1, x0 : x0 + 1, 0] = 0
        atlas[y0 : y0 + 1, x0:x1, 3] = 255

    img = Image.fromarray(atlas, mode="RGBA")
    img.save(out_png_path, format="PNG", optimize=True)
    return out_png_path


def auto_skin_15piece_armor(
    base_spec: ManifestEntry,
    output_dir: Path,
) -> tuple[list[Path], Path]:
    """15 parca zirh setini (Katman 1..3 offset kurallariyla) ve 64x64 Gap-Map atlasini uretir."""
    output_dir.mkdir(parents=True, exist_ok=True)
    generator = Armor15PieceGenerator()
    generated_glbs: list[Path] = []

    for slot_name in ARMOR_15_SLOTS:
        slot_spec = ManifestEntry(
            asset_id=f"armor_slot_{slot_name}",
            category="Armor15Piece",
            phase=base_spec.phase,
            generator_path=base_spec.generator_path,
            target_dimensions_meters=base_spec.target_dimensions_meters,
            dimension_tolerance_ratio=base_spec.dimension_tolerance_ratio,
            pivot_rule=base_spec.pivot_rule,
            forward_axis=base_spec.forward_axis,
            up_axis=base_spec.up_axis,
            poly_budget_lod0=base_spec.poly_budget_lod0,
            poly_budget_lod1=base_spec.poly_budget_lod1,
            poly_budget_lod2=base_spec.poly_budget_lod2,
            default_material_id=base_spec.default_material_id,
            required_sockets=list(base_spec.required_sockets),
            dismemberable_bones=[],
            bake_vat=False,
        )
        out_glb = output_dir / f"{slot_spec.asset_id}.glb"
        generator.generate(slot_spec, out_glb)
        generated_glbs.append(out_glb)

    gap_atlas = bake_armor_gap_map_atlas(output_dir / "T_ArmorGapMap_64x64_Atlas.png", resolution=64)
    return generated_glbs, gap_atlas
