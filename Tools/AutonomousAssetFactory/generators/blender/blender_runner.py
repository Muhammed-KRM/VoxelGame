"""Headless Blender 4.2 LTS Calistiricisi (blender_runner.py - Sifir Sabit Yol, Goreceli Cozumleme)."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import Any

from generators.base_generator import ManifestEntry


def _repo_root() -> Path:
    return Path(__file__).resolve().parents[4]


def resolve_blender_executable() -> Path | None:
    """Blender 4.2 calistirilabilir dosyasini goreceli olarak bulur (sabit surucu yolu icermez)."""
    env_override = os.environ.get("STRATO_BLENDER")
    if env_override:
        p = Path(env_override)
        if p.is_file():
            return p

    portable = _repo_root() / "Tools" / "bin" / "blender-4.2" / "blender.exe"
    if portable.is_file():
        return portable

    which_bl = shutil.which("blender")
    if which_bl:
        return Path(which_bl)

    return None


def run_blender_sculpt_batch(jobs: list[tuple[ManifestEntry, Path]], timeout_sec: int = 180) -> bool:
    """Verilen ManifestEntry listesini TEK bir Headless Blender 4.2 surecinde hizla uretir."""
    blender_exe = resolve_blender_executable()
    if blender_exe is None or not jobs:
        return False

    worker_script = Path(__file__).resolve().parent / "bpy_sculpt_worker.py"
    serial_jobs: list[dict[str, Any]] = []
    for spec, out_glb in jobs:
        serial_jobs.append(
            {
                "asset_id": spec.asset_id,
                "category": spec.category,
                "target_dimensions_meters": list(spec.target_dimensions_meters),
                "poly_budget_lod0": spec.poly_budget_lod0,
                "default_material_id": spec.default_material_id,
                "required_sockets": list(spec.required_sockets),
                "out_glb_path": str(out_glb),
            }
        )

    with tempfile.TemporaryDirectory(prefix="strato_bpy_") as tmp_dir:
        jobs_json = Path(tmp_dir) / "sculpt_jobs.json"
        jobs_json.write_text(json.dumps({"jobs": serial_jobs}, indent=2), encoding="utf-8")

        cmd = [
            str(blender_exe),
            "--background",
            "--factory-startup",
            "--python-exit-code",
            "1",
            "--python",
            str(worker_script),
            "--",
            "--batch",
            str(jobs_json),
        ]
        proc = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            timeout=timeout_sec,
            check=False,
        )
        if proc.returncode != 0:
            print(f"[BlenderRunner HATA] returncode={proc.returncode}\n{proc.stderr}", file=sys.stderr)
            return False

        return all(out_glb.is_file() and out_glb.stat().st_size > 128 for _, out_glb in jobs)
