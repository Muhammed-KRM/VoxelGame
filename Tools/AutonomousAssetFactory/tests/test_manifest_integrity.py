"""Stratocraft_Asset_Manifest.json sema, butce ve VRAM siniri butunluk testleri."""

from __future__ import annotations

import json
from pathlib import Path

from generators.base_generator import ManifestEntry


def test_manifest_json_schema_and_budgets_are_valid() -> None:
    """Manifestodaki tum kayitlar Bolum 4.8 sozlesmesine uymalidir."""
    manifest_file = Path(__file__).resolve().parents[1] / "Stratocraft_Asset_Manifest.json"
    assert manifest_file.exists(), "Stratocraft_Asset_Manifest.json bulunamadi!"

    data = json.loads(manifest_file.read_text(encoding="utf-8"))
    assert data["vramTextureBudgetMB"] <= 16.0
    assets = data.get("assets", [])
    assert len(assets) >= 1

    seen_ids: set[str] = set()
    for raw in assets:
        entry = ManifestEntry.from_dict(raw)
        assert entry.asset_id not in seen_ids, f"Tekrarlanan assetId: {entry.asset_id}"
        seen_ids.add(entry.asset_id)

        tx, ty, tz = entry.target_dimensions_meters
        assert tx > 0.0 and ty > 0.0 and tz > 0.0
        assert 0.0 < entry.dimension_tolerance_ratio <= 0.05
        assert entry.forward_axis == "+Z"
        assert entry.up_axis == "+Y"
        assert entry.poly_budget_lod0 >= entry.poly_budget_lod1 >= entry.poly_budget_lod2 > 0
        assert 1 <= entry.default_material_id <= 255
        for sock in entry.required_sockets:
            assert sock.startswith("Socket_"), f"Soket adi Socket_ ile baslamali: {sock}"


def test_global_baked_textures_respect_16mb_vram_budget() -> None:
    """Kuresel UberShader dokularinin disk/VRAM boyutu 16.0 MB altinda (Faz 0 <= 6.5 MB) olmalidir."""
    repo_root = Path(__file__).resolve().parents[3]
    tex_dir = repo_root / "Assets" / "Art" / "Generated" / "Textures"
    expected_files = [
        "T_Stratocraft_MaterialLUT_256x16.png",
        "T_Stratocraft_MatCap_16x.png",
        "T_Stratocraft_MicroDetail_Array32.dds",
        "T_Stratocraft_Noise3D_64.bin",
    ]
    for fname in expected_files:
        fpath = tex_dir / fname
        assert fpath.exists(), f"Kuresel doku eksik: {fpath}"
        assert fpath.stat().st_size > 256

    total_mb = sum((tex_dir / f).stat().st_size for f in expected_files) / (1024.0 * 1024.0)
    assert total_mb <= 6.5, f"Faz 0 doku boyutu 6.5 MB hedefini asti: {total_mb:.3f} MB"
    assert total_mb < 16.0
