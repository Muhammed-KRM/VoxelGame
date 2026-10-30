"""Stratocraft Otomatik Rigging, GPU Uzuv Koparma (Dismemberment) ve VAT Dokusu Firinlama Modulu."""

from .stratocraft_armor_autoskin import auto_skin_15piece_armor, bake_armor_gap_map_atlas
from .stratocraft_auto_rig_dismember import (
    CANONICAL_HUMANOID_BONES,
    DISMEMBERABLE_BONES,
    GORE_CAP_SOCKETS,
    DismemberRigResult,
    build_rigged_dismemberable_assets,
)
from .vat_texture_baker import VatBakeResult, bake_night_wave_500mob_vat_textures

__all__ = [
    "CANONICAL_HUMANOID_BONES",
    "DISMEMBERABLE_BONES",
    "GORE_CAP_SOCKETS",
    "DismemberRigResult",
    "VatBakeResult",
    "auto_skin_15piece_armor",
    "bake_armor_gap_map_atlas",
    "bake_night_wave_500mob_vat_textures",
    "build_rigged_dismemberable_assets",
]
