"""Pytest ortak fiksturleri ve modul yolu ayarlari (Tools/AutonomousAssetFactory/tests/conftest.py)."""

from __future__ import annotations

import sys
from pathlib import Path

import pytest

FACTORY_ROOT = Path(__file__).resolve().parents[1]
if str(FACTORY_ROOT) not in sys.path:
    sys.path.insert(0, str(FACTORY_ROOT))

from generators.base_generator import ManifestEntry  # noqa: E402


@pytest.fixture()
def sample_manifest_entry() -> ManifestEntry:
    """Birim testleri icin ornek moduler silah kabzasi manifesto sozlesmesi."""
    return ManifestEntry(
        asset_id="test_hilt_ironwood",
        category="WeaponHilt",
        phase=0,
        generator_path="PathA_ProceduralGlb",
        target_dimensions_meters=(0.24, 0.32, 0.05),
        dimension_tolerance_ratio=0.02,
        pivot_rule="BottomCenter_Zero",
        forward_axis="+Z",
        up_axis="+Y",
        poly_budget_lod0=320,
        poly_budget_lod1=160,
        poly_budget_lod2=60,
        default_material_id=71,
        required_sockets=["Socket_Grip_Main", "Socket_VoxelBlade"],
    )
