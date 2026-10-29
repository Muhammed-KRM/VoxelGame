"""asset_validator.py ve kendi kendini duzelten (self-correction) 3B boru hatti birim testleri."""

from __future__ import annotations

from dataclasses import replace
from pathlib import Path

import numpy as np

from agent_generate_asset import run_self_correction_loop
from asset_validator import validate_glb_asset
from contact_sheet_renderer import render_4angle_contact_sheet
from generators.base_generator import ManifestEntry, build_calibration_glb, write_color32_glb


def test_valid_generated_glb_passes_all_validator_checks(
    tmp_path: Path, sample_manifest_entry: ManifestEntry
) -> None:
    """Kurallara uygun uretilen Color32 .glb ve 4-acili PNG dogrulamadan %100 gecmelidir."""
    glb_path = tmp_path / "valid_hilt.glb"
    png_path = tmp_path / "valid_hilt_contact_sheet.png"

    build_calibration_glb(sample_manifest_entry, glb_path)
    render_4angle_contact_sheet(glb_path, png_path, prefer_blender_cli=False)

    res = validate_glb_asset(glb_path, sample_manifest_entry, contact_sheet_png=png_path)
    assert res.is_valid, f"Beklenmeyen dogrulama hatasi: {res.errors}"
    assert res.has_color32 is True
    assert res.triangle_count <= sample_manifest_entry.poly_budget_lod0
    assert png_path.exists() and png_path.stat().st_size > 1024


def test_validator_catches_wrong_scale_missing_socket_and_poly_overflow(
    tmp_path: Path, sample_manifest_entry: ManifestEntry
) -> None:
    """Yanlis olcek, eksik Socket_* ve asiri ucgen sayisi hatalari net kodlarla yakalanmalidir."""
    bad_spec = replace(sample_manifest_entry, target_dimensions_meters=(1.50, 2.00, 0.80))
    glb_path = tmp_path / "wrong_scale.glb"
    build_calibration_glb(bad_spec, glb_path)

    strict_spec = replace(
        sample_manifest_entry,
        poly_budget_lod0=10,
        required_sockets=["Socket_Grip_Main", "Socket_VoxelBlade", "Socket_MissingTest"],
    )
    res = validate_glb_asset(glb_path, strict_spec)
    assert not res.is_valid
    joined = " | ".join(res.errors)
    assert "ASSET_AABB_OUT_OF_TOLERANCE" in joined
    assert "ASSET_POLY_BUDGET_EXCEEDED" in joined
    assert "ASSET_MISSING_SOCKETS" in joined


def test_self_correction_loop_fixes_wrong_scale_pivot_and_missing_socket(
    tmp_path: Path, sample_manifest_entry: ManifestEntry
) -> None:
    """Bozuk olcekli, kayik pivotlu ve eksik soketli bir .glb otomatik duzeltilip gecmelidir."""
    glb_path = tmp_path / "misaligned.glb"
    png_path = tmp_path / "misaligned_sheet.png"

    # Kasitli olarak kayik pivotlu (y=2.0..5.0) ve yanlis boyutlu tek kup yaz
    positions = np.array(
        [
            [1.0, 2.0, 1.0],
            [3.0, 2.0, 1.0],
            [3.0, 5.0, 1.0],
            [1.0, 5.0, 1.0],
            [1.0, 2.0, 4.0],
            [3.0, 2.0, 4.0],
            [3.0, 5.0, 4.0],
            [1.0, 5.0, 4.0],
        ],
        dtype=np.float32,
    )
    normals = np.tile(np.array([[0.0, 1.0, 0.0]], dtype=np.float32), (8, 1))
    colors = np.tile(np.array([[71, 0, 128, 255]], dtype=np.uint8), (8, 1))
    indices = np.array(
        [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7, 0, 4, 7, 0, 7, 3, 1, 5, 6, 1, 6, 2],
        dtype=np.uint16,
    )
    write_color32_glb(glb_path, "misaligned", positions, normals, colors, indices, sockets={})

    initial_check = validate_glb_asset(glb_path, sample_manifest_entry)
    assert not initial_check.is_valid

    fixed_res = run_self_correction_loop(sample_manifest_entry, glb_path, png_path, max_attempts=3)
    assert fixed_res.is_valid, f"Otomatik duzeltme basarisiz oldu: {fixed_res.errors}"
