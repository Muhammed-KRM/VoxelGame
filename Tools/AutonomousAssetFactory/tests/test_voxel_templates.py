"""Stratocraft 75 Fiziksel Batarya, 25 Mayin, 13 Boss Kafesi ve Ors (.svox) Sablon Testleri."""

from __future__ import annotations

import struct
from pathlib import Path

from generators.voxel_template_generator import (
    SVOX_MAGIC,
    SVOX_VERSION,
    build_75_battery_definitions,
    compile_25_mine_and_13_cage_templates,
    compile_75_battery_templates,
)


def test_75_batteries_have_unique_vertical_poles_and_yaw_invariant_signatures() -> None:
    """75 bataryanin tamami dikey kutup kilitli olmali ve 0/90/180/270 derece donuste cakismamalidir."""
    batteries = build_75_battery_definitions()
    assert len(batteries) == 75

    signatures = set()
    pole_pairs = set()
    for bat in batteries:
        assert bat.top_pole_mat != bat.bottom_pole_mat
        sig = bat.canonical_signature()
        assert sig not in signatures, f"Donme-degismez imza cakismasi: {bat.name}"
        signatures.add(sig)
        pole_pairs.add((bat.top_pole_mat, bat.bottom_pole_mat, bat.tier, bat.category))

    assert len(signatures) == 75
    assert len(pole_pairs) == 75


def test_svox_bundles_compile_with_valid_binary_headers(tmp_path: Path) -> None:
    """75 batarya, 25 mayin + 6 cekirdek, 18 kafes ve ors .svox paketleri gecerli baslikla derlenmelidir."""
    bat_svox = compile_75_battery_templates(tmp_path)
    mines_svox, cages_svox, anvil_svox = compile_25_mine_and_13_cage_templates(tmp_path)

    for path, expected_count in (
        (bat_svox, 75),
        (mines_svox, 31),
        (cages_svox, 18),
        (anvil_svox, 1),
    ):
        assert path.exists()
        raw = path.read_bytes()
        magic, ver, tpl_count, meta_len = struct.unpack("<4sHHI", raw[:12])
        assert magic == SVOX_MAGIC
        assert ver == SVOX_VERSION
        assert tpl_count == expected_count
        assert len(raw) > 12 + meta_len
