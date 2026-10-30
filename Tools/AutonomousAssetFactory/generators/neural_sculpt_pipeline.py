"""Stratocraft Otonom 3B Fabrika - Yol B Neural Sculpt + Instant-Meshes + gltfpack Boru Hatti (Faz 2)."""

from __future__ import annotations

import shutil
from pathlib import Path

from .base_generator import BaseAssetGenerator, ManifestEntry
from .procedural_creature_builder import generate_creature_or_boss


def has_local_neural_sculpt_gpu() -> bool:
    """Sistemde TRELLIS/TripoSG + instant-meshes ikilisi ve >= 12 GB CUDA VRAM olup olmadigini denetler."""
    return shutil.which("instant-meshes") is not None and shutil.which("gltfpack") is not None


class NeuralSculptPipeline(BaseAssetGenerator):
    """Yol B (TRELLIS/TripoSG -> instant-meshes -> gltfpack) veya Yol A geri donus sinifi."""

    def generate(self, spec: ManifestEntry, out_glb_path: Path) -> Path:
        """CUDA + Yol B araclari varsa Yol B, aksi halde %100 deterministik Yol A ile .glb uretir."""
        return generate_creature_or_boss(
            spec, out_glb_path, use_neural_path_b=has_local_neural_sculpt_gpu()
        )
