"""Stratocraft Rigging, GPU Uzuv Koparma, 15-Parca Zirh ve VAT Firinlama Birim Testleri."""

from __future__ import annotations

from pathlib import Path

from asset_validator import validate_glb_asset
from generators.base_generator import ManifestEntry
from generators.procedural_creature_builder import ProceduralCreatureBuilder
from rigging_and_vat.stratocraft_armor_autoskin import auto_skin_15piece_armor
from rigging_and_vat.stratocraft_auto_rig_dismember import (
    CANONICAL_HUMANOID_BONES,
    GORE_CAP_SOCKETS,
    build_rigged_dismemberable_assets,
)
from rigging_and_vat.vat_texture_baker import bake_night_wave_500mob_vat_textures


def test_dismember_rig_pipeline_creates_valid_creature_and_severed_limbs(tmp_path: Path) -> None:
    """Canli modeli + GoreCap soketleri + kopan uzuv (SeveredLimbs/*.glb) uretilip dogrulanmalidir."""
    spec = ManifestEntry(
        asset_id="mob_ash_stalker",
        category="Creature",
        phase=2,
        generator_path="PathA_BlenderBpy",
        target_dimensions_meters=(0.90, 1.85, 0.75),
        dimension_tolerance_ratio=0.02,
        pivot_rule="BottomCenter_Zero",
        forward_axis="+Z",
        up_axis="+Y",
        poly_budget_lod0=1200,
        poly_budget_lod1=600,
        poly_budget_lod2=240,
        default_material_id=71,
        required_sockets=[
            "Socket_Head",
            "Socket_Chest",
            *GORE_CAP_SOCKETS,
        ],
        dismemberable_bones=["Head", "UpperArm.L", "UpperArm.R", "UpperLeg.L", "UpperLeg.R"],
        bake_vat=True,
    )

    main_glb = tmp_path / "mob_ash_stalker.glb"
    ProceduralCreatureBuilder().generate(spec, main_glb)

    val = validate_glb_asset(main_glb, spec)
    assert val.is_valid, f"Canli modeli dogrulamadan gecemedi: {val.errors}"

    rig_res = build_rigged_dismemberable_assets(spec, main_glb, tmp_path / "SeveredLimbs")
    assert len(rig_res.bone_names) == len(CANONICAL_HUMANOID_BONES)
    assert len(rig_res.severed_limb_paths) == 5
    for limb_glb in rig_res.severed_limb_paths:
        assert limb_glb.exists() and limb_glb.stat().st_size > 256


def test_15piece_armor_and_vat_baker_stay_within_vram_budget(tmp_path: Path) -> None:
    """15 parca zirh seti, 64x64 Gap-Map atlasi ve 500-Mob VAT dokulari VRAM butcesine uymalidir."""
    armor_spec = ManifestEntry(
        asset_id="armor_set_iron_vanguard",
        category="Armor15Piece",
        phase=2,
        generator_path="PathA_BlenderBpy",
        target_dimensions_meters=(0.55, 0.65, 0.35),
        dimension_tolerance_ratio=0.02,
        pivot_rule="BottomCenter_Zero",
        forward_axis="+Z",
        up_axis="+Y",
        poly_budget_lod0=600,
        poly_budget_lod1=300,
        poly_budget_lod2=120,
        default_material_id=32,
        required_sockets=["Socket_ArmorAttach"],
    )
    glbs, gap_png = auto_skin_15piece_armor(armor_spec, tmp_path / "Armors_15Piece")
    assert len(glbs) == 15
    assert gap_png.exists() and gap_png.stat().st_size < 32 * 1024

    vat_res = bake_night_wave_500mob_vat_textures(tmp_path / "Creatures_21")
    assert vat_res.pos_exr_path.exists()
    assert vat_res.norm_exr_path.exists()
    assert vat_res.total_vram_bytes < 512 * 1024
