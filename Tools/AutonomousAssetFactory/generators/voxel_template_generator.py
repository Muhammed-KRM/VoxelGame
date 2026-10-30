"""Stratocraft Otonom 3B Fabrika - 75 Batarya, 25 Mayin ve Ors (.svox) Ureticisi (Faz 2)."""

from __future__ import annotations

import json
import struct
from dataclasses import dataclass
from pathlib import Path

import numpy as np

SVOX_MAGIC = b"SVOX"
SVOX_VERSION = 1


@dataclass(slots=True, frozen=True)
class SparseVoxelEntry:
    """Tek bir bagil voxel koordinati (dx, dy, dz), MaterialId ve OriginState (1 = OyuncuItemBlogu)."""

    dx: int
    dy: int
    dz: int
    material_id: int
    origin_state: int = 1


@dataclass(slots=True)
class VoxelTemplateDefinition:
    """75 Batarya, 25 Mayin veya 13 Boss egitme kafesi icin 3B sablon tanimi."""

    template_id: int
    name: str
    category: str
    tier: int
    top_pole_mat: int
    bottom_pole_mat: int
    voxels: list[SparseVoxelEntry]

    def rotated_yaw(self, quarter_turns: int) -> list[tuple[int, int, int, int]]:
        """Sablonu Y ekseni etrafinda 90 derecenin katlari (0, 1, 2, 3) kadar dondurur."""
        turns = quarter_turns & 3
        result: list[tuple[int, int, int, int]] = []
        for v in self.voxels:
            x, y, z = v.dx, v.dy, v.dz
            if turns == 1:
                x, z = -z, x
            elif turns == 2:
                x, z = -x, -z
            elif turns == 3:
                x, z = z, -x
            result.append((x, y, z, v.material_id))
        result.sort()
        return result

    def canonical_signature(self) -> tuple[tuple[int, int, int, int], ...]:
        """4 yatay yonde (0, 90, 180, 270 derece) donme-degismez (Yaw-Invariant) kanonik imzayi dondurur."""
        candidates = [tuple(self.rotated_yaw(r)) for r in range(4)]
        return min(candidates)


def _write_svox_bundle(out_path: Path, templates: list[VoxelTemplateDefinition]) -> Path:
    """Sablon listesini ikili .svox formatinda (SVOX baslik + JSON indeks + voxel dizisi) yazar."""
    out_path.parent.mkdir(parents=True, exist_ok=True)
    meta_list = []
    voxel_Payloads = bytearray()

    for tpl in templates:
        byte_offset = len(voxel_Payloads)
        for v in tpl.voxels:
            voxel_Payloads.extend(
                struct.pack("<bbbBB", v.dx, v.dy, v.dz, v.material_id & 0xFF, v.origin_state & 0x03)
            )
        meta_list.append(
            {
                "id": tpl.template_id,
                "name": tpl.name,
                "category": tpl.category,
                "tier": tpl.tier,
                "topPole": tpl.top_pole_mat,
                "bottomPole": tpl.bottom_pole_mat,
                "voxelCount": len(tpl.voxels),
                "byteOffset": byte_offset,
            }
        )

    meta_bytes = json.dumps(meta_list, ensure_ascii=True, separators=(",", ":")).encode("utf-8")
    header = struct.pack("<4sHHI", SVOX_MAGIC, SVOX_VERSION, len(templates), len(meta_bytes))
    out_path.write_bytes(header + meta_bytes + bytes(voxel_Payloads))
    return out_path


def build_75_battery_definitions() -> list[VoxelTemplateDefinition]:
    """Bolum 10.3'teki 75 bataryanin dikey kutup kilitli 3B sablonlarini uretir."""
    categories = ["Offensive", "Construction", "Support"]
    templates: list[VoxelTemplateDefinition] = []

    # Her batarya (1..75) icin benzersiz (top_pole_mat, bottom_pole_mat) dikey kutup cifti garanti edilir
    # boylece hicbir batarya 90/180/270 derece donduruldugunde baska bir bataryayi alt-kume olarak iceremez.
    battery_id = 1
    for cat_idx, category in enumerate(categories):
        for tier in range(1, 6):
            for sub_idx in range(5):
                top_mat = 30 + ((battery_id * 3 + cat_idx) % 180)
                bot_mat = 30 + ((battery_id * 7 + tier * 11 + sub_idx) % 180)
                if top_mat == bot_mat:
                    bot_mat = 30 + ((bot_mat + 1) % 180)

                voxels: list[SparseVoxelEntry] = [
                    SparseVoxelEntry(0, 0, 0, material_id=200, origin_state=1),  # Merkez Batarya Cekirdegi
                    SparseVoxelEntry(0, 1, 0, material_id=top_mat, origin_state=1),  # Ust Kutup (+Y)
                    SparseVoxelEntry(0, -1, 0, material_id=bot_mat, origin_state=1),  # Alt Kutup (-Y)
                ]

                wing_mat = 30 + ((battery_id * 5 + sub_idx) % 180)
                if tier >= 2:
                    voxels.append(SparseVoxelEntry(1, 0, 0, wing_mat, 1))
                    voxels.append(SparseVoxelEntry(-1, 0, 0, wing_mat, 1))
                if tier >= 3:
                    voxels.append(SparseVoxelEntry(0, 0, 1, wing_mat, 1))
                    voxels.append(SparseVoxelEntry(0, 0, -1, wing_mat, 1))
                if tier >= 4:
                    voxels.append(SparseVoxelEntry(1, 1, 0, wing_mat, 1))
                    voxels.append(SparseVoxelEntry(-1, 1, 0, wing_mat, 1))
                if tier >= 5:
                    voxels.append(SparseVoxelEntry(1, 0, 1, wing_mat, 1))
                    voxels.append(SparseVoxelEntry(-1, 0, 1, wing_mat, 1))
                    voxels.append(SparseVoxelEntry(1, 0, -1, wing_mat, 1))
                    voxels.append(SparseVoxelEntry(-1, 0, -1, wing_mat, 1))

                templates.append(
                    VoxelTemplateDefinition(
                        template_id=battery_id,
                        name=f"Battery_{category}_T{tier}_{sub_idx + 1}",
                        category=category,
                        tier=tier,
                        top_pole_mat=top_mat,
                        bottom_pole_mat=bot_mat,
                        voxels=voxels,
                    )
                )
                battery_id += 1

    return templates


def build_25_mine_and_6_core_definitions() -> list[VoxelTemplateDefinition]:
    """5 Alt Blok x 5 Tetikleyici Oz = 25 Fiziksel Mayin ve 6 Yakitli Tuzak Cekirdegi sablonunu uretir."""
    templates: list[VoxelTemplateDefinition] = []
    base_blocks = [32, 36, 37, 39, 42]  # Demir, Titanyum, Mithril, Cehennem Demiri, Adamantit
    trigger_cores = [44, 30, 35, 41, 45]  # Komur/Ates, Bakir/Sok, Altin/Manyetik, Astral, Karanlik Madde

    mine_id = 1
    for tier_idx, base_mat in enumerate(base_blocks, start=1):
        for core_idx, trig_mat in enumerate(trigger_cores, start=1):
            voxels = [
                SparseVoxelEntry(0, -1, 0, material_id=base_mat, origin_state=1),
                SparseVoxelEntry(0, 0, 0, material_id=trig_mat, origin_state=1),
            ]
            templates.append(
                VoxelTemplateDefinition(
                    template_id=mine_id,
                    name=f"Mine_T{tier_idx}_C{core_idx}",
                    category="PhysicalMine25",
                    tier=tier_idx,
                    top_pole_mat=trig_mat,
                    bottom_pole_mat=base_mat,
                    voxels=voxels,
                )
            )
            mine_id += 1

    for core_type in range(1, 7):
        frame_mat = 50 + core_type
        voxels = [
            SparseVoxelEntry(0, 0, 0, material_id=210 + core_type, origin_state=1),
            SparseVoxelEntry(1, 0, 0, material_id=frame_mat, origin_state=1),
            SparseVoxelEntry(-1, 0, 0, material_id=frame_mat, origin_state=1),
            SparseVoxelEntry(0, 0, 1, material_id=frame_mat, origin_state=1),
            SparseVoxelEntry(0, 0, -1, material_id=frame_mat, origin_state=1),
        ]
        templates.append(
            VoxelTemplateDefinition(
                template_id=100 + core_type,
                name=f"FueledTrapCore_{core_type}",
                category="FueledTrapCore6",
                tier=min(5, core_type),
                top_pole_mat=210 + core_type,
                bottom_pole_mat=frame_mat,
                voxels=voxels,
            )
        )
    return templates


def compile_75_battery_templates(out_dir: Path) -> Path:
    """75 batarya sablonunu batteries_75_templates.svox olarak derler."""
    return _write_svox_bundle(out_dir / "batteries_75_templates.svox", build_75_battery_definitions())


def compile_25_mine_and_13_cage_templates(out_dir: Path) -> tuple[Path, Path, Path]:
    """25 mayin + 6 tuzak cekirdegi, 5+13 egitme kafesi ve standart ors sablonlarini .svox olarak derler."""
    mines_path = _write_svox_bundle(
        out_dir / "traps_6cores_and_25mines.svox", build_25_mine_and_6_core_definitions()
    )

    cages: list[VoxelTemplateDefinition] = []
    for i in range(1, 19):
        mat = 70 + (i % 15)
        cages.append(
            VoxelTemplateDefinition(
                template_id=200 + i,
                name=f"TamingCage_{i}",
                category="TamingCage",
                tier=min(5, (i - 1) // 3 + 1),
                top_pole_mat=mat,
                bottom_pole_mat=mat,
                voxels=[
                    SparseVoxelEntry(0, 0, 0, material_id=220, origin_state=1),
                    SparseVoxelEntry(2, 0, 2, material_id=mat, origin_state=1),
                    SparseVoxelEntry(-2, 0, 2, material_id=mat, origin_state=1),
                    SparseVoxelEntry(2, 0, -2, material_id=mat, origin_state=1),
                    SparseVoxelEntry(-2, 0, -2, material_id=mat, origin_state=1),
                ],
            )
        )
    cages_path = _write_svox_bundle(out_dir / "taming_5general_and_13boss_cages.svox", cages)

    # Standart 16x32x4 demirci orsu hedef sablonu
    grid_mask = np.zeros((16, 32), dtype=np.uint8)
    grid_mask[6:10, 2:30] = 1
    anvil_voxels = [
        SparseVoxelEntry(int(x - 8), 0, int(z - 16), material_id=51, origin_state=1)
        for x in range(16)
        for z in range(32)
        if grid_mask[x, z] == 1
    ]
    anvil_path = _write_svox_bundle(
        out_dir / "anvil_standard_weapon_templates.svox",
        [
            VoxelTemplateDefinition(
                template_id=301,
                name="StandardLongswordBlade_16x32x4",
                category="AnvilBladeTemplate",
                tier=2,
                top_pole_mat=51,
                bottom_pole_mat=51,
                voxels=anvil_voxels,
            )
        ],
    )
    return mines_path, cages_path, anvil_path
