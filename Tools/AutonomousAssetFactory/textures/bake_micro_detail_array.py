"""Stratocraft 32-Katmanli Biplanar Micro-Detail DDS Dizisi ve 64^3 3B Gurultu Hacmi Firinlayici."""

from __future__ import annotations

import struct
from pathlib import Path

import numpy as np


def _tileable_hash_noise(res: int, freq: int, seed: int) -> np.ndarray:
    """Periyodik (dikissiz / seamless) 2B deger gurultusu [0.0, 1.0]."""
    rng = np.random.default_rng(seed)
    grid = rng.random((freq, freq), dtype=np.float32)

    coords = np.linspace(0.0, float(freq), res, endpoint=False, dtype=np.float32)
    i0 = np.floor(coords).astype(np.int32) % freq
    i1 = (i0 + 1) % freq
    f = coords - np.floor(coords)
    u = f * f * (3.0 - 2.0 * f)

    gx0, gy0 = np.meshgrid(i0, i0)
    gx1, gy1 = np.meshgrid(i1, i1)
    ux, uy = np.meshgrid(u, u)

    n00 = grid[gy0, gx0]
    n10 = grid[gy0, gx1]
    n01 = grid[gy1, gx0]
    n11 = grid[gy1, gx1]
    nx0 = n00 + ux * (n10 - n00)
    nx1 = n01 + ux * (n11 - n01)
    return nx0 + uy * (nx1 - nx0)


def bake_micro_detail_array32_dds(out_dds_path: Path, res: int = 128, layers: int = 32) -> Path:
    """DX10 DDS Texture2DArray (32 katman, NormalXY + RoughnessDelta + Cavity) uretir (~2.0 MB VRAM).

    Toplam kaplama VRAM butcesini (< 16.0 MB) kesin olarak korumak icin 128x128x32 RGBA8 DX10 DDS
    konteyneri uretir (128 * 128 * 4 * 32 = 2.097.152 Byte = 2.0 MB VRAM!).
    """
    out_dds_path.parent.mkdir(parents=True, exist_ok=True)

    # DDS sabitleri (DDS_MAGIC + DDS_HEADER + DDS_HEADER_DXT10)
    dds_magic = b"DDS "
    ddsd_caps = 0x1
    ddsd_height = 0x2
    ddsd_width = 0x4
    ddsd_pitch = 0x8
    ddsd_pixelformat = 0x1000
    ddsd_mipmapcount = 0x20000
    flags = ddsd_caps | ddsd_height | ddsd_width | ddsd_pitch | ddsd_pixelformat | ddsd_mipmapcount
    pitch = res * 4

    # DDS_PIXELFORMAT (FOURCC = "DX10")
    ddpf = struct.pack("<II4sIIIII", 32, 0x4, b"DX10", 0, 0, 0, 0, 0)
    caps1 = 0x1000  # DDSCAPS_TEXTURE

    header = struct.pack(
        "<IIIIIII11I",
        124,        # dwSize
        flags,      # dwFlags
        res,        # dwHeight
        res,        # dwWidth
        pitch,      # dwPitchOrLinearSize
        1,          # dwDepth
        1,          # dwMipMapCount
        *(0 for _ in range(11)),
    )
    header += ddpf
    header += struct.pack("<IIIII", caps1, 0, 0, 0, 0)

    # DDS_HEADER_DXT10: DXGI_FORMAT_R8G8B8A8_UNORM = 28, D3D10_RESOURCE_DIMENSION_TEXTURE2D = 3
    dx10_header = struct.pack("<IIIII", 28, 3, 0, layers, 0)

    slices_data = bytearray()
    for layer_idx in range(layers):
        base = _tileable_hash_noise(res, freq=8 + (layer_idx % 4) * 4, seed=1000 + layer_idx * 17)
        fine = _tileable_hash_noise(res, freq=24, seed=5000 + layer_idx * 31)
        height = 0.65 * base + 0.35 * fine

        # Anisotropik cizgiler (fircalanmis metal / ahsap lifleri icin)
        if layer_idx >= 20:
            lin = np.linspace(0.0, np.pi * 16.0, res, endpoint=False, dtype=np.float32)
            stripes = 0.5 + 0.5 * np.sin(lin)[:, None]
            height = 0.7 * height + 0.3 * stripes

        dx = np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)
        dy = np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)

        nx = np.clip(0.5 + dx * 2.2, 0.0, 1.0)
        ny = np.clip(0.5 + dy * 2.2, 0.0, 1.0)
        rough_mod = np.clip(height, 0.0, 1.0)
        cavity = np.clip(1.0 - np.abs(dx) * 3.0 - np.abs(dy) * 3.0, 0.2, 1.0)

        rgba = np.stack([nx, ny, rough_mod, cavity], axis=-1)
        slices_data.extend(np.clip(rgba * 255.0, 0, 255).astype(np.uint8).tobytes())

    out_dds_path.write_bytes(dds_magic + header + dx10_header + bytes(slices_data))
    return out_dds_path


def bake_noise3d_64_volume(out_asset_path: Path, size: int = 64) -> Path:
    """64^3 3B kati doku (R=Sam Celigi, G=Ahsap Halkasi, B=Kristal Hucre, A=Perlin FBM) uretir (1.0 MB)."""
    out_asset_path.parent.mkdir(parents=True, exist_ok=True)
    lin = np.linspace(0.0, 2.0 * np.pi, size, endpoint=False, dtype=np.float32)
    z, y, x = np.meshgrid(lin, lin, lin, indexing="ij")

    # R: 3B Sam Celigi (Damascus Sine-Wave katman kivrimlari)
    turb = np.sin(x * 2.0 + np.cos(y * 3.0)) + np.cos(z * 2.0 + np.sin(x * 3.0))
    damascus = 0.5 + 0.5 * np.sin(y * 6.0 + turb * 2.4)

    # G: 3B Silindirik Ahsap Yas Halkalari
    cx = x - np.pi
    cz = z - np.pi
    radial = np.sqrt(cx * cx + cz * cz)
    wood = 0.5 + 0.5 * np.sin(radial * 7.0 + turb * 0.8)

    # B: 3B Kristal Kirinim Fasetleri
    crystal = np.abs(np.sin(x * 4.0) * np.cos(y * 4.0) * np.sin(z * 4.0))

    # A: Genel Amacli 3B Mikro-Varyasyon
    micro = 0.5 + 0.25 * np.sin(x * 5.0 + y * 3.0) + 0.25 * np.cos(y * 5.0 - z * 4.0)

    vol_u8 = np.clip(np.stack([damascus, wood, crystal, micro], axis=-1) * 255.0, 0, 255).astype(np.uint8)
    magic_header = struct.pack("<4sIIII", b"SN3D", 1, size, size, size)
    out_asset_path.write_bytes(magic_header + vol_u8.tobytes())
    return out_asset_path
