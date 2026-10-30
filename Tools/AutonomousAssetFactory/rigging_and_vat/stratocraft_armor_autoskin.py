"""Stratocraft 15 Parca Zirh Otomatik Agirlik Aktarimi ve Gap-Map Atlasi (Bolum 4.6 & 5.3)."""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image

from generators.armor_15piece_generator import ARMOR_15_SLOTS, Armor15PieceGenerator
from generators.base_generator import ManifestEntry
from generators.blender.blender_runner import run_blender_sculpt_batch


def _slot_target_dimensions(slot_name: str) -> tuple[float, float, float]:
    """Her bir anatomik zirh bolgesi ve katmani (Katman 1..3) icin gercekci metre olculeri."""
    layer_inflate = 0.030 if "layer3" in slot_name else (0.015 if "layer2" in slot_name else 0.0)
    if slot_name.startswith("head_"):
        base = (0.28, 0.30, 0.30)
    elif slot_name.startswith("torso_"):
        base = (0.50, 0.58, 0.34)
    elif slot_name.startswith("arms_"):
        base = (0.48, 0.50, 0.24)
    elif slot_name.startswith("hands_"):
        base = (0.34, 0.26, 0.16)
    else:
        base = (0.42, 0.76, 0.28)
    return (
        round(base[0] + layer_inflate, 4),
        round(base[1] + layer_inflate * 0.5, 4),
        round(base[2] + layer_inflate, 4),
    )


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
    """15 parca zirh setini (Head/Torso/Arms/Hands/Legs x Katman 1..3) Blender 4.2 ile uretir."""
    output_dir.mkdir(parents=True, exist_ok=True)
    batch_jobs: list[tuple[ManifestEntry, Path]] = []
    generated_glbs: list[Path] = []

    for slot_name in ARMOR_15_SLOTS:
        slot_mat = 75 if "layer1" in slot_name else (32 if "layer2" in slot_name else 51)
        slot_spec = ManifestEntry(
            asset_id=f"armor_slot_{slot_name}",
            category="Armor15Piece",
            phase=base_spec.phase,
            generator_path=base_spec.generator_path,
            target_dimensions_meters=_slot_target_dimensions(slot_name),
            dimension_tolerance_ratio=base_spec.dimension_tolerance_ratio,
            pivot_rule=base_spec.pivot_rule,
            forward_axis=base_spec.forward_axis,
            up_axis=base_spec.up_axis,
            poly_budget_lod0=max(240, base_spec.poly_budget_lod0 // 2),
            poly_budget_lod1=max(120, base_spec.poly_budget_lod1 // 2),
            poly_budget_lod2=max(60, base_spec.poly_budget_lod2 // 2),
            default_material_id=slot_mat,
            required_sockets=list(base_spec.required_sockets),
            dismemberable_bones=[],
            bake_vat=False,
        )
        out_glb = output_dir / f"{slot_spec.asset_id}.glb"
        batch_jobs.append((slot_spec, out_glb))
        generated_glbs.append(out_glb)

    if not run_blender_sculpt_batch(batch_jobs):
        generator = Armor15PieceGenerator()
        for slot_spec, out_glb in batch_jobs:
            generator.generate(slot_spec, out_glb)

    gap_atlas = bake_armor_gap_map_atlas(output_dir / "T_ArmorGapMap_64x64_Atlas.png", resolution=64)
    return generated_glbs, gap_atlas
