"""Stratocraft Otonom 3B Varlik ve Kuresel Doku Fabrikasi Ana Orkestratoru (agent_generate_asset.py)."""

from __future__ import annotations

import argparse
import json
import sys
from dataclasses import dataclass, field
from pathlib import Path

from asset_validator import ValidationResult, auto_correct_glb_transform, validate_glb_asset
from contact_sheet_renderer import render_4angle_contact_sheet
from generators.base_generator import ManifestEntry, build_calibration_glb
from textures.bake_material_lut import bake_matcap_atlas_16x, bake_material_lut_256x16
from textures.bake_micro_detail_array import bake_micro_detail_array32_dds, bake_noise3d_64_volume


@dataclass(slots=True)
class AssetBatchReport:
    """Faz bazli otonom doku ve 3B model uretim raporu."""

    phase: int
    total_texture_vram_mb: float
    generated_assets: list[str] = field(default_factory=list)
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
    """Tek bir manifesto kaydini hedef klasore .glb olarak uretir."""
    _ = has_cuda_gpu  # Yol A deterministik uretim iki bilgisayarda da varsayilandir
    repo = _repo_root()
    sub_folder = _category_to_output_folder(entry.category)
    out_glb = repo / "Assets" / "Art" / "Generated" / sub_folder / f"{entry.asset_id}.glb"
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
    """Belirtilen faza ait kuresel dokulari ve manifesto 3B modellerini uretip dogrular."""
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

    for entry in entries:
        glb_path = generate_single_asset(entry, has_cuda_gpu=(not force_cpu_fallback))
        preview_png = previews_dir / f"{entry.asset_id}_contact_sheet.png"
        val_res = run_self_correction_loop(entry, glb_path, preview_png, max_attempts=3)
        if val_res.is_valid:
            report.generated_assets.append(entry.asset_id)
        else:
            report.failed_assets[entry.asset_id] = val_res.errors

    return report


def main() -> int:
    """Komut satiri giris noktasi."""
    parser = argparse.ArgumentParser(description="Stratocraft Otonom 3B Varlik Fabrikasi")
    parser.add_argument("--phase", type=int, default=0, help="Uretilecek faz numarasi (0..10)")
    args = parser.parse_args()

    report = generate_assets_for_phase(phase=args.phase, force_cpu_fallback=True)
    print(
        f"[AssetFactory] Faz={report.phase} | Kuresel Kaplama VRAM={report.total_texture_vram_mb:.3f} MB | "
        f"Basarili Varlik={len(report.generated_assets)} | Hatali={len(report.failed_assets)}"
    )
    if not report.is_success:
        for asset_id, errs in report.failed_assets.items():
            print(f"  [HATA] {asset_id}: {errs}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
