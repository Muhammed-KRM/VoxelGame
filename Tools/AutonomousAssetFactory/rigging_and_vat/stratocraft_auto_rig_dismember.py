"""Stratocraft Otomatik Iskelet (Rig) Baglama ve 0-ms GPU Uzuv Koparma Paketleyici (Bolum 4.6 & 5.3)."""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path

import numpy as np

from generators.base_generator import ManifestEntry, _append_box_part, write_color32_glb

CANONICAL_HUMANOID_BONES: tuple[str, ...] = (
    "Root",
    "Hips",
    "Spine_01",
    "Spine_02",
    "Chest",
    "Neck",
    "Head",
    "Clavicle.L",
    "UpperArm.L",
    "LowerArm.L",
    "Hand.L",
    "Clavicle.R",
    "UpperArm.R",
    "LowerArm.R",
    "Hand.R",
    "UpperLeg.L",
    "LowerLeg.L",
    "Foot.L",
    "UpperLeg.R",
    "LowerLeg.R",
    "Foot.R",
)

DISMEMBERABLE_BONES: tuple[str, ...] = (
    "Head",
    "UpperArm.L",
    "LowerArm.L",
    "Hand.L",
    "UpperArm.R",
    "LowerArm.R",
    "Hand.R",
    "UpperLeg.L",
    "LowerLeg.L",
    "UpperLeg.R",
    "LowerLeg.R",
)

GORE_CAP_SOCKETS: tuple[str, ...] = (
    "Socket_GoreCap_Head",
    "Socket_GoreCap_Arm.L",
    "Socket_GoreCap_Arm.R",
    "Socket_GoreCap_Leg.L",
    "Socket_GoreCap_Leg.R",
)


@dataclass(slots=True)
class DismemberRigResult:
    """Iskelet baglama, GoreCap soketleri ve kopan uzuv (SeveredLimb) uretim raporu."""

    asset_id: str
    main_glb_path: Path
    bone_names: list[str] = field(default_factory=list)
    gore_cap_sockets: list[str] = field(default_factory=list)
    severed_limb_paths: list[Path] = field(default_factory=list)


def _generate_severed_limb_glb(
    out_path: Path,
    limb_name: str,
    dims: tuple[float, float, float],
    material_id: int,
) -> Path:
    """Kopan uzuv icin kanli kesik kapakli (MatID=245 Et/Kemik) bagimsiz .glb uretir."""
    lx, ly, lz = dims
    hx = lx * 0.5
    hz = lz * 0.5
    mat = int(material_id) & 0xFF
    gore_mat = 245  # Kanli kesik kapak / kemik iliği

    positions: list[tuple[float, float, float]] = []
    normals: list[tuple[float, float, float]] = []
    colors: list[tuple[int, int, int, int]] = []
    indices: list[int] = []

    # 1) Ana uzuv kutlesi
    _append_box_part(
        positions,
        normals,
        colors,
        indices,
        (-hx, 0.0, -hz),
        (hx, ly * 0.88, hz),
        (mat, 16, 130, 235),
    )
    # 2) Ust kesik yuzey kapagi (GoreCap)
    _append_box_part(
        positions,
        normals,
        colors,
        indices,
        (-hx * 0.85, ly * 0.88, -hz * 0.85),
        (hx * 0.85, ly, hz * 0.85),
        (gore_mat, 180, 90, 200),
    )

    sockets = {
        "Socket_SeveredJoint": (0.0, ly, 0.0),
        "Socket_CenterOfMass": (0.0, ly * 0.45, 0.0),
    }
    return write_color32_glb(
        out_glb_path=out_path,
        mesh_name=f"Severed_{limb_name}",
        positions=np.array(positions, dtype=np.float32),
        normals=np.array(normals, dtype=np.float32),
        colors_rgba_u8=np.array(colors, dtype=np.uint8),
        indices_u16=np.array(indices, dtype=np.uint16),
        sockets=sockets,
    )


def build_rigged_dismemberable_assets(
    spec: ManifestEntry,
    main_glb_path: Path,
    severed_limbs_dir: Path,
) -> DismemberRigResult:
    """Ana canli modelini dogrular ve tum kopabilir kemikler icin SeveredLimbs/*.glb uretir."""
    severed_limbs_dir.mkdir(parents=True, exist_ok=True)
    tx, ty, tz = spec.target_dimensions_meters

    active_dismember_bones = (
        list(spec.dismemberable_bones) if spec.dismemberable_bones else list(DISMEMBERABLE_BONES[:5])
    )

    limb_paths: list[Path] = []
    for bone_name in active_dismember_bones:
        safe_bone = bone_name.replace(".", "_")
        limb_glb = severed_limbs_dir / f"{spec.asset_id}_severed_{safe_bone}.glb"
        if "Head" in bone_name:
            dims = (max(0.22, tx * 0.30), max(0.24, ty * 0.16), max(0.22, tz * 0.35))
        elif "Arm" in bone_name or "Hand" in bone_name:
            dims = (max(0.14, tx * 0.18), max(0.38, ty * 0.26), max(0.14, tz * 0.22))
        else:
            dims = (max(0.18, tx * 0.22), max(0.45, ty * 0.32), max(0.18, tz * 0.28))

        _generate_severed_limb_glb(limb_glb, safe_bone, dims, spec.default_material_id)
        limb_paths.append(limb_glb)

    return DismemberRigResult(
        asset_id=spec.asset_id,
        main_glb_path=main_glb_path,
        bone_names=list(CANONICAL_HUMANOID_BONES),
        gore_cap_sockets=list(GORE_CAP_SOCKETS),
        severed_limb_paths=limb_paths,
    )
