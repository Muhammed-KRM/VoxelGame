"""Stratocraft Otonom 3B Varlik Uretici Modulu (Faz 0 -> Faz 10)."""

from .armor_15piece_generator import (
    ARMOR_15_SLOTS,
    Armor15PieceGenerator,
    bake_armor_gap_map_64x64,
    build_15piece_armor_mesh,
    build_15piece_armor_set,
)
from .base_generator import BaseAssetGenerator, ManifestEntry, build_calibration_glb
from .neural_sculpt_pipeline import NeuralSculptPipeline, has_local_neural_sculpt_gpu
from .procedural_creature_builder import ProceduralCreatureBuilder, generate_creature_or_boss
from .voxel_template_generator import (
    SVOX_MAGIC,
    SVOX_VERSION,
    SparseVoxelEntry,
    VoxelTemplateDefinition,
    build_25_mine_and_6_core_definitions,
    build_75_battery_definitions,
    compile_25_mine_and_13_cage_templates,
    compile_75_battery_templates,
)
from .weapon_hilt_generator import WeaponHiltGenerator, build_modular_weapon_hilt
from .workshop_and_clan_generator import (
    WorkshopAndClanGenerator,
    build_siege_weapon_5stage_dfa,
    build_workshop_or_clan_structure,
)

__all__ = [
    "ARMOR_15_SLOTS",
    "Armor15PieceGenerator",
    "BaseAssetGenerator",
    "ManifestEntry",
    "NeuralSculptPipeline",
    "ProceduralCreatureBuilder",
    "SVOX_MAGIC",
    "SVOX_VERSION",
    "SparseVoxelEntry",
    "VoxelTemplateDefinition",
    "WeaponHiltGenerator",
    "WorkshopAndClanGenerator",
    "bake_armor_gap_map_64x64",
    "build_15piece_armor_mesh",
    "build_15piece_armor_set",
    "build_25_mine_and_6_core_definitions",
    "build_75_battery_definitions",
    "build_calibration_glb",
    "build_modular_weapon_hilt",
    "build_siege_weapon_5stage_dfa",
    "build_workshop_or_clan_structure",
    "compile_25_mine_and_13_cage_templates",
    "compile_75_battery_templates",
    "generate_creature_or_boss",
    "has_local_neural_sculpt_gpu",
]
