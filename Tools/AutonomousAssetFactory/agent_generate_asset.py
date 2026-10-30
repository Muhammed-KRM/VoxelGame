"""Stratocraft Otonom 3B Varlik ve Kuresel Doku Fabrikasi Ana Orkestratoru (agent_generate_asset.py)."""

from __future__ import annotations

import argparse
import json
import shutil
import sys
from dataclasses import dataclass, field
from pathlib import Path

from asset_validator import ValidationResult, auto_correct_glb_transform, validate_glb_asset
from contact_sheet_renderer import render_4angle_contact_sheet
from generators.armor_15piece_generator import Armor15PieceGenerator
from generators.base_generator import ManifestEntry, build_calibration_glb
from generators.neural_sculpt_pipeline import NeuralSculptPipeline
from generators.procedural_creature_builder import ProceduralCreatureBuilder
from generators.voxel_template_generator import (
    compile_25_mine_and_13_cage_templates,
    compile_75_battery_templates,
)
from generators.weapon_hilt_generator import WeaponHiltGenerator
from generators.workshop_and_clan_generator import WorkshopAndClanGenerator
from rigging_and_vat.stratocraft_armor_autoskin import auto_skin_15piece_armor
from rigging_and_vat.stratocraft_auto_rig_dismember import build_rigged_dismemberable_assets
from rigging_and_vat.vat_texture_baker import bake_night_wave_500mob_vat_textures
from textures.bake_material_lut import bake_matcap_atlas_16x, bake_material_lut_256x16
from textures.bake_micro_detail_array import bake_micro_detail_array32_dds, bake_noise3d_64_volume


@dataclass(slots=True)
class AssetBatchReport:
    """Faz bazli otonom doku ve 3B model uretim raporu."""

    phase: int
    total_texture_vram_mb: float
    generated_assets: list[str] = field(default_factory=list)
    generated_voxel_templates: list[str] = field(default_factory=list)
    failed_assets: dict[str, list[str]] = field(default_factory=dict)

    @property
    def is_success(self) -> bool:
        """Tum uretimler ve < 16.0 MB VRAM butcesi basariliysa True dondurur."""
        return len(self.failed_assets) == 0 and self.total_texture_vram_mb < 16.0


def _repo_root() -> Path:
    return Path(__file__).resolve().parents[2]


def _category_to_output_folder(category: str) -> str:
    folder_map = {
        "CalibrationProp": "Calibration",
        "WeaponHilt": "Weapons_Modular",
        "Armor15Piece": "Armors_15Piece",
        "WorkshopDevice": "Workshops_29Devices",
        "ClanStructure": "Structures_24Active",
        "SiegeWeapon": "Siege_And_Vehicles",
        "Creature": "Creatures_21",
        "Boss": "Bosses_13_And_Disasters_12",
    }
    return folder_map.get(category, "Misc")


def bake_global_ubershader_textures(repo_root: Path) -> float:
    """Tum oyunun ortak paylastigi 4 kuresel dokuyu (< 6.5 MB VRAM) uretir ve MB boyutunu dondurur."""
    tex_dir = repo_root / "Assets" / "Art" / "Generated" / "Textures"
    lut_path = bake_material_lut_256x16(tex_dir / "T_Stratocraft_MaterialLUT_256x16.png")
    matcap_path = bake_matcap_atlas_16x(tex_dir / "T_Stratocraft_MatCap_16x.png", tile_res=128)
    micro_path = bake_micro_detail_array32_dds(tex_dir / "T_Stratocraft_MicroDetail_Array32.dds", res=128)
    noise_path = bake_noise3d_64_volume(tex_dir / "T_Stratocraft_Noise3D_64.bin", size=64)

    total_bytes = sum(p.stat().st_size for p in (lut_path, matcap_path, micro_path, noise_path))
    return round(total_bytes / (1024.0 * 1024.0), 4)


def generate_single_asset(entry: ManifestEntry, has_cuda_gpu: bool = False) -> Path:
    """Tek bir manifesto kaydini kategori ureticisiyle hedef klasore .glb olarak uretir."""
    repo = _repo_root()
    sub_folder = _category_to_output_folder(entry.category)
    out_glb = repo / "Assets" / "Art" / "Generated" / sub_folder / f"{entry.asset_id}.glb"

    if entry.category == "WeaponHilt":
        return WeaponHiltGenerator().generate(entry, out_glb)
    if entry.category == "Armor15Piece":
        return Armor15PieceGenerator().generate(entry, out_glb)
    if entry.category in ("WorkshopDevice", "ClanStructure", "SiegeWeapon"):
        return WorkshopAndClanGenerator().generate(entry, out_glb)
    if entry.category in ("Creature", "Boss"):
        if has_cuda_gpu:
            return NeuralSculptPipeline().generate(entry, out_glb)
        return ProceduralCreatureBuilder().generate(entry, out_glb)

    return build_calibration_glb(entry, out_glb)


def run_self_correction_loop(
    entry: ManifestEntry,
    glb_path: Path,
    preview_png_path: Path,
    max_attempts: int = 3,
) -> ValidationResult:
    """Uretilen .glb dosyasini dogrular; olcek/pivot/soket sapmasi varsa otomatik duzeltir."""
    result = ValidationResult(is_valid=False, asset_id=entry.asset_id)
    for _attempt in range(max_attempts):
        render_4angle_contact_sheet(glb_path, preview_png_path, prefer_blender_cli=False)
        result = validate_glb_asset(glb_path, entry, contact_sheet_png=preview_png_path)
        if result.is_valid:
            return result
        auto_correct_glb_transform(glb_path, entry)

    render_4angle_contact_sheet(glb_path, preview_png_path, prefer_blender_cli=False)
    return validate_glb_asset(glb_path, entry, contact_sheet_png=preview_png_path)


def generate_assets_for_phase(phase: int, force_cpu_fallback: bool = True) -> AssetBatchReport:
    """Belirtilen faza ait kuresel dokulari, .svox sablonlarini ve manifesto 3B modellerini uretir."""
    repo = _repo_root()
    total_vram_mb = bake_global_ubershader_textures(repo)

    manifest_path = Path(__file__).resolve().parent / "Stratocraft_Asset_Manifest.json"
    manifest_data = json.loads(manifest_path.read_text(encoding="utf-8"))
    entries = [
        ManifestEntry.from_dict(item)
        for item in manifest_data.get("assets", [])
        if int(item["phase"]) == phase
    ]

    report = AssetBatchReport(phase=phase, total_texture_vram_mb=total_vram_mb)
    previews_dir = repo / "BuildLogs" / "Previews"
    reports_sheets_dir = repo / "Reports" / "AssetContactSheets"
    reports_sheets_dir.mkdir(parents=True, exist_ok=True)

    for entry in entries:
        glb_path = generate_single_asset(entry, has_cuda_gpu=(not force_cpu_fallback))
        preview_png = previews_dir / f"{entry.asset_id}_contact_sheet.png"
        val_res = run_self_correction_loop(entry, glb_path, preview_png, max_attempts=3)
        if val_res.is_valid:
            report.generated_assets.append(entry.asset_id)
            shutil.copy2(preview_png, reports_sheets_dir / f"{entry.asset_id}_4View.png")
            if entry.category == "Armor15Piece":
                auto_skin_15piece_armor(entry, glb_path.parent)
            if entry.dismemberable_bones:
                limbs_dir = glb_path.parent / "SeveredLimbs"
                build_rigged_dismemberable_assets(entry, glb_path, limbs_dir)
            if entry.bake_vat:
                vat_res = bake_night_wave_500mob_vat_textures(glb_path.parent)
                report.total_texture_vram_mb = round(
                    report.total_texture_vram_mb + vat_res.total_vram_bytes / (1024.0 * 1024.0),
                    4,
                )
        else:
            report.failed_assets[entry.asset_id] = val_res.errors

    if phase >= 2:
        svox_dir = repo / "Assets" / "StreamingAssets" / "VoxelTemplates"
        bat_path = compile_75_battery_templates(svox_dir)
        mines_path, cages_path, anvil_path = compile_25_mine_and_13_cage_templates(svox_dir)
        report.generated_voxel_templates.extend(
            [bat_path.name, mines_path.name, cages_path.name, anvil_path.name]
        )

    return report


def main() -> int:
    """Komut satiri giris noktasi."""
    parser = argparse.ArgumentParser(description="Stratocraft Otonom 3B Varlik Fabrikasi")
    parser.add_argument("--phase", type=int, default=0, help="Uretilecek faz numarasi (0..10)")
    args = parser.parse_args()

    report = generate_assets_for_phase(phase=args.phase, force_cpu_fallback=True)
    print(
        f"[AssetFactory] Faz={report.phase} | Toplam Kaplama VRAM={report.total_texture_vram_mb:.3f} MB | "
        f"Basarili Varlik={len(report.generated_assets)} | "
        f"SVOX Sablon={len(report.generated_voxel_templates)} | "
        f"Hatali={len(report.failed_assets)}"
    )
    if not report.is_success:
        for asset_id, errs in report.failed_assets.items():
            print(f"  [HATA] {asset_id}: {errs}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
