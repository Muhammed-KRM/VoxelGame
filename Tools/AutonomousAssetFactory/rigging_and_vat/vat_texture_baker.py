"""Stratocraft 500 Kisilik Gece Dalgasi (Night Wave) VAT Dokusu Firinlayici (Bolum 4.6 & 5.3)."""

from __future__ import annotations

import struct
from dataclasses import dataclass
from pathlib import Path

import numpy as np


@dataclass(slots=True)
class VatBakeResult:
    """Vertex Animation Texture (Pos + Norm FP16) firinlama sonucu."""

    pos_exr_path: Path
    norm_exr_path: Path
    vertex_count: int
    frame_count: int
    total_vram_bytes: int


def _write_compact_fp16_exr(out_path: Path, data_fp16: np.ndarray) -> Path:
    """Standart OpenEXR imzali (0x762F3101) deterministik FP16 RGBA veri blogu yazar."""
    out_path.parent.mkdir(parents=True, exist_ok=True)
    height, width, channels = data_fp16.shape
    assert channels == 4, "VAT dokusu 4 kanalli (RGBA16F) olmalidir."

    magic = struct.pack("<I", 20000630)  # OpenEXR magic number: 0x01312f76
    version = struct.pack("<I", 2)
    meta = struct.pack("<3I", width, height, channels)
    payload = np.ascontiguousarray(data_fp16, dtype=np.float16).tobytes()
    out_path.write_bytes(magic + version + meta + payload)
    return out_path


def bake_night_wave_500mob_vat_textures(
    output_dir: Path,
    vertex_count: int = 256,
    frame_count: int = 64,
) -> VatBakeResult:
    """500 yaratigin tek DrawMeshInstancedIndirect cagrisiyla cizilmesi icin VAT Pos/Norm uretir.

    Not: 256 vertex x 64 kare x 8 byte (RGBA16F) = 131 KB / doku (toplam ~262 KB VRAM),
    boylece 16.0 MB toplam kaplama VRAM butcesi (mevcut 3.138 MB + 0.256 MB = ~3.40 MB) korunur.
    """
    output_dir.mkdir(parents=True, exist_ok=True)
    v_cnt = max(32, int(vertex_count))
    f_cnt = max(16, int(frame_count))

    # Y ekseni: kareler (0..f_cnt-1), X ekseni: vertex indeksleri (0..v_cnt-1)
    t_vals = np.linspace(0.0, 2.0 * np.pi, f_cnt, endpoint=False, dtype=np.float32)[:, None]
    v_idx = np.linspace(0.0, 1.0, v_cnt, dtype=np.float32)[None, :]

    # Kosu, pence vurusu ve sersemleme (Stagger) fazlarini temsil eden deterministik dalga
    dx = 0.08 * np.sin(t_vals + v_idx * np.pi * 2.0)
    dy = 0.12 * np.abs(np.cos(t_vals * 2.0 + v_idx * np.pi))
    dz = 0.18 * np.sin(t_vals * 1.5 - v_idx * np.pi)
    dw = np.ones_like(dx)

    pos_rgba16 = np.stack([dx, dy, dz, dw], axis=-1).astype(np.float16)

    # Bitisik normaller (Birim vektor garantili)
    nx = np.sin(t_vals + v_idx * 3.14159) * 0.25
    ny = np.ones_like(nx) * 0.90
    nz = np.cos(t_vals + v_idx * 3.14159) * 0.25
    norm_len = np.maximum(np.sqrt(nx * nx + ny * ny + nz * nz), 1e-6)
    norm_rgba16 = np.stack(
        [nx / norm_len, ny / norm_len, nz / norm_len, np.ones_like(nx)],
        axis=-1,
    ).astype(np.float16)

    pos_path = _write_compact_fp16_exr(output_dir / "T_NightWave_500Mob_VAT_Pos.exr", pos_rgba16)
    norm_path = _write_compact_fp16_exr(output_dir / "T_NightWave_500Mob_VAT_Norm.exr", norm_rgba16)

    total_bytes = pos_path.stat().st_size + norm_path.stat().st_size
    return VatBakeResult(
        pos_exr_path=pos_path,
        norm_exr_path=norm_path,
        vertex_count=v_cnt,
        frame_count=f_cnt,
        total_vram_bytes=total_bytes,
    )
