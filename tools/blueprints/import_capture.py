#!/usr/bin/env python3
"""Turn one bounded Quest Lab capture into deterministic Godbuild source.

The capture sidecar is the lossless replay authority. The PlanBuild file is its
portable projection; plan.json and preview.svg make the same build reviewable and
scriptable without asking a creator to relay geometry through the seat.
"""
from __future__ import annotations

import argparse
import hashlib
import html
import json
import math
import os
from collections import Counter
from decimal import Decimal, InvalidOperation, ROUND_HALF_UP
from pathlib import Path
import re
import shutil
import subprocess
import sys


CAPTURE_SCHEMA = "comfy-questlab-capture/v1"
PLAN_SCHEMA = "comfy-quest-godbuild-plan/v1"
MANIFEST_SCHEMA = "comfy-quest-godbuild/v1"
STANDALONE_BUNDLE_SCHEMA = "comfy-quest-standalone-rnd-bundle/v1"
MAX_PIECES = 2048
TOP_KEYS = {
    "Schema", "Name", "Selection", "RadiusMetres", "PieceCount",
    "PiecesSha256", "Pieces",
}
PIECE_KEYS = {
    "Prefab", "Category", "X", "Y", "Z", "Qx", "Qy", "Qz", "Qw",
    "HasSignText", "SignText", "HasItemStand", "ItemPrefab", "ItemVariant",
    "ItemQuality", "ItemType", "RuneSchool", "RuneStyle", "TextGlowSchool",
}
SAFE_NAME = re.compile(r"^[a-z0-9_-]{1,64}$")


class CaptureError(ValueError):
    pass


def repository_root() -> Path:
    return Path(__file__).resolve().parents[2]


def assert_repo_identity(root: Path) -> None:
    script = root / "tools" / "Assert-RepoIdentity.ps1"
    shell = shutil.which("pwsh") or shutil.which("powershell")
    if not shell:
        raise CaptureError("PowerShell is required for repository identity verification")
    result = subprocess.run(
        [shell, "-NoProfile", "-File", str(script)],
        cwd=root,
        text=True,
        capture_output=True,
        check=False,
    )
    if result.returncode:
        detail = (result.stderr or result.stdout).strip()
        raise CaptureError(f"repository identity rejected: {detail}")


def assert_standalone_bundle(manifest_path: Path) -> None:
    """Verify this exact importer inside a source-less, hash-pinned R&D bundle."""
    try:
        raw = manifest_path.resolve().read_bytes()
        if len(raw) > 64 * 1024:
            raise CaptureError("standalone bundle manifest exceeds 64 KiB")
        manifest = json.loads(raw.decode("utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise CaptureError(f"standalone bundle manifest is unreadable: {error}") from error
    if not isinstance(manifest, dict):
        raise CaptureError("standalone bundle manifest must be an object")
    required = {
        "schema", "repository_id", "source_revision", "source_dirty",
        "source_tree_sha256", "host_bundle_sha256", "host_bundle_bytes", "files",
    }
    exact_keys(manifest, required, "standalone bundle manifest")
    if manifest["schema"] != STANDALONE_BUNDLE_SCHEMA:
        raise CaptureError("standalone bundle schema is unsupported")
    if manifest["repository_id"] != "djcdevelopment/comfy-quest":
        raise CaptureError("standalone bundle repository identity differs")
    for field in ("source_revision", "source_tree_sha256", "host_bundle_sha256"):
        value = manifest[field]
        if not isinstance(value, str) or not re.fullmatch(r"[0-9a-f]{64}" if field != "source_revision" else r"[0-9a-f]{40}", value):
            raise CaptureError(f"standalone bundle {field} is invalid")
    if not isinstance(manifest["source_dirty"], bool):
        raise CaptureError("standalone bundle source_dirty must be boolean")
    if not isinstance(manifest["host_bundle_bytes"], int) or manifest["host_bundle_bytes"] < 1:
        raise CaptureError("standalone bundle host_bundle_bytes is invalid")
    files = manifest["files"]
    if not isinstance(files, dict) or set(files) != {"tools/blueprints/import_capture.py"}:
        raise CaptureError("standalone bundle files must pin only the authoritative importer")
    pin = files["tools/blueprints/import_capture.py"]
    if not isinstance(pin, dict):
        raise CaptureError("standalone importer pin must be an object")
    exact_keys(pin, {"bytes", "sha256"}, "standalone importer pin")
    script = Path(__file__).resolve()
    expected = manifest_path.resolve().parent / "tools" / "blueprints" / "import_capture.py"
    if script != expected.resolve():
        raise CaptureError("standalone importer path differs from the bundle manifest")
    payload = script.read_bytes()
    if pin["bytes"] != len(payload) or pin["sha256"] != hashlib.sha256(payload).hexdigest():
        raise CaptureError("standalone importer bytes differ from the bundle manifest")


def exact_keys(value: dict, expected: set[str], label: str) -> None:
    missing = sorted(expected - set(value))
    extra = sorted(set(value) - expected)
    if missing or extra:
        raise CaptureError(f"{label} keys differ: missing={missing}, extra={extra}")


def safe_token(value: object, limit: int, label: str, optional: bool = False) -> str:
    if value is None and optional:
        return ""
    if not isinstance(value, str) or (not value and not optional) or len(value) > limit:
        raise CaptureError(f"{label} is not a bounded string")
    if any(ord(char) < 32 or char in ";\r\n" for char in value):
        raise CaptureError(f"{label} contains unsafe characters")
    return value


def finite_number(value: object, label: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise CaptureError(f"{label} must be numeric")
    result = float(value)
    if not math.isfinite(result):
        raise CaptureError(f"{label} must be finite")
    return result


def bounded_int(value: object, low: int, high: int, label: str) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or not low <= value <= high:
        raise CaptureError(f"{label} must be an integer in {low}..{high}")
    return value


def number(value: object, digits: int) -> str:
    try:
        decimal = Decimal(str(value)).quantize(
            Decimal(1).scaleb(-digits), rounding=ROUND_HALF_UP
        )
    except InvalidOperation as error:
        raise CaptureError(f"invalid number: {value!r}") from error
    if decimal == 0:
        return "0"
    text = format(decimal, "f").rstrip("0").rstrip(".")
    return text if text not in {"", "-0"} else "0"


def escaped(value: str) -> str:
    return value.replace("\\", "\\\\").replace("\t", "\\t").replace("\r", "\\r").replace("\n", "\\n")


def signature(piece: dict) -> str:
    return "\t".join(
        [
            piece["Prefab"], piece["Category"],
            number(piece["X"], 4), number(piece["Y"], 4), number(piece["Z"], 4),
            number(piece["Qx"], 6), number(piece["Qy"], 6),
            number(piece["Qz"], 6), number(piece["Qw"], 6),
            "1" if piece["HasSignText"] else "0", escaped(piece["SignText"]),
            "1" if piece["HasItemStand"] else "0", piece["ItemPrefab"],
            str(piece["ItemVariant"]), str(piece["ItemQuality"]), str(piece["ItemType"]),
            piece["RuneSchool"], piece["RuneStyle"], piece["TextGlowSchool"],
        ]
    )


def pieces_hash(pieces: list[dict]) -> str:
    payload = "\n".join(signature(piece) for piece in pieces).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


def rounded(value: object, digits: int) -> float:
    result = float(number(value, digits))
    return 0.0 if result == 0 else result


def canonical_quaternion(piece: dict) -> None:
    values = [float(piece[key]) for key in ("Qx", "Qy", "Qz", "Qw")]
    norm = math.sqrt(sum(value * value for value in values))
    if norm < 0.000001:
        values = [0.0, 0.0, 0.0, 1.0]
    else:
        values = [value / norm for value in values]
    values = [rounded(value, 6) for value in values]
    qx, qy, qz, qw = values
    if qw < 0 or (qw == 0 and (qz < 0 or (qz == 0 and (qy < 0 or (qy == 0 and qx < 0))))):
        values = [rounded(-value, 6) for value in values]
    for key, value in zip(("Qx", "Qy", "Qz", "Qw"), values):
        piece[key] = value


def normalize_and_sort(pieces: list[dict]) -> list[dict]:
    canonical = [dict(piece) for piece in pieces]
    minima = {axis: min(float(piece[axis]) for piece in canonical) for axis in ("X", "Y", "Z")}
    for piece in canonical:
        for axis in ("X", "Y", "Z"):
            piece[axis] = rounded(float(piece[axis]) - minima[axis], 4)
        canonical_quaternion(piece)
        piece["Category"] = piece["Category"] or "Building"
        for key in ("SignText", "ItemPrefab", "RuneSchool", "RuneStyle", "TextGlowSchool"):
            piece[key] = piece[key] or ""
    canonical.sort(key=signature)
    return canonical


def validate_architectural_source(value: object) -> dict:
    """Validate the deliberately non-importable architectural handoff envelope."""
    if not isinstance(value, dict):
        raise CaptureError("architectural source must be a JSON object")
    exact_keys(value, TOP_KEYS, "architectural source")
    if value["Schema"] != CAPTURE_SCHEMA:
        raise CaptureError(f"unsupported capture schema: {value['Schema']!r}")
    if not isinstance(value["Name"], str) or not SAFE_NAME.fullmatch(value["Name"]):
        raise CaptureError("Name must use 1-64 lowercase letters, digits, '-' or '_'")
    if value["Selection"] != "architectural-import-candidate":
        raise CaptureError("architectural derivation requires Selection='architectural-import-candidate'")
    radius = finite_number(value["RadiusMetres"], "RadiusMetres")
    if not 1 <= radius <= 40:
        raise CaptureError("RadiusMetres must be in 1..40")
    pieces = value["Pieces"]
    if not isinstance(pieces, list) or not 1 <= len(pieces) <= MAX_PIECES:
        raise CaptureError(f"Pieces must contain 1..{MAX_PIECES} records")
    if value["PieceCount"] != len(pieces):
        raise CaptureError("PieceCount does not match Pieces")
    if not isinstance(value["PiecesSha256"], str) or not re.fullmatch(
        r"[0-9a-fA-F]{64}", value["PiecesSha256"]
    ):
        raise CaptureError("architectural PiecesSha256 must be a SHA-256 provenance token")
    for index, piece in enumerate(pieces):
        if not isinstance(piece, dict):
            raise CaptureError(f"piece {index} must be an object")
        exact_keys(piece, PIECE_KEYS, f"piece {index}")
        safe_token(piece["Prefab"], 128, f"piece {index} Prefab")
        safe_token(piece["Category"], 64, f"piece {index} Category")
        for key in ("ItemPrefab", "RuneSchool", "RuneStyle", "TextGlowSchool"):
            safe_token(piece[key], 128 if key == "ItemPrefab" else 32, f"piece {index} {key}", True)
        if not isinstance(piece["SignText"], str) or len(piece["SignText"]) > 1024:
            raise CaptureError(f"piece {index} SignText is invalid")
        if not isinstance(piece["HasSignText"], bool) or not isinstance(piece["HasItemStand"], bool):
            raise CaptureError(f"piece {index} metadata flags must be boolean")
        if not piece["HasItemStand"] and piece["ItemPrefab"]:
            raise CaptureError(f"piece {index} has an item without HasItemStand")
        bounded_int(piece["ItemVariant"], 0, 255, f"piece {index} ItemVariant")
        bounded_int(piece["ItemQuality"], 0, 100, f"piece {index} ItemQuality")
        bounded_int(piece["ItemType"], 0, 255, f"piece {index} ItemType")
        for key in ("X", "Y", "Z", "Qx", "Qy", "Qz", "Qw"):
            finite_number(piece[key], f"piece {index} {key}")
        norm = sum(float(piece[key]) ** 2 for key in ("Qx", "Qy", "Qz", "Qw"))
        if not 0.90 <= norm <= 1.10:
            raise CaptureError(f"piece {index} has a non-unit rotation")
    return value


def derive_architectural_capture(value: object, name: str, yaw_degrees: float) -> dict:
    source = validate_architectural_source(value)
    if not SAFE_NAME.fullmatch(name or ""):
        raise CaptureError("derived Name must use 1-64 lowercase letters, digits, '-' or '_'")
    if name == source["Name"]:
        raise CaptureError("derived Name must differ from the immutable architectural source")
    if not math.isfinite(yaw_degrees) or abs(yaw_degrees) > 3600:
        raise CaptureError("derived yaw must be finite and within +/-3600 degrees")
    angle = math.radians(yaw_degrees)
    sine, cosine = math.sin(angle), math.cos(angle)
    half_sine, half_cosine = math.sin(angle / 2), math.cos(angle / 2)
    rotated = []
    for source_piece in source["Pieces"]:
        piece = dict(source_piece)
        x, z = float(piece["X"]), float(piece["Z"])
        piece["X"] = cosine * x + sine * z
        piece["Z"] = -sine * x + cosine * z
        qx, qy, qz, qw = (float(piece[key]) for key in ("Qx", "Qy", "Qz", "Qw"))
        piece["Qx"] = half_cosine * qx + half_sine * qz
        piece["Qy"] = half_cosine * qy + half_sine * qw
        piece["Qz"] = half_cosine * qz - half_sine * qx
        piece["Qw"] = half_cosine * qw - half_sine * qy
        rotated.append(piece)
    pieces = normalize_and_sort(rotated)
    derived = {
        "Schema": CAPTURE_SCHEMA,
        "Name": name,
        "Selection": "lab",
        "RadiusMetres": source["RadiusMetres"],
        "PieceCount": len(pieces),
        "PiecesSha256": pieces_hash(pieces),
        "Pieces": pieces,
    }
    return validate_capture(derived)


def validate_capture(value: object) -> dict:
    if not isinstance(value, dict):
        raise CaptureError("capture must be a JSON object")
    exact_keys(value, TOP_KEYS, "capture")
    if value["Schema"] != CAPTURE_SCHEMA:
        raise CaptureError(f"unsupported capture schema: {value['Schema']!r}")
    if not isinstance(value["Name"], str) or not SAFE_NAME.fullmatch(value["Name"]):
        raise CaptureError("Name must use 1-64 lowercase letters, digits, '-' or '_'")
    if value["Selection"] not in {"mine", "lab"}:
        raise CaptureError("Selection must be 'mine' or 'lab'")
    radius = finite_number(value["RadiusMetres"], "RadiusMetres")
    if not 1 <= radius <= 40:
        raise CaptureError("RadiusMetres must be in 1..40")
    pieces = value["Pieces"]
    if not isinstance(pieces, list) or not 1 <= len(pieces) <= MAX_PIECES:
        raise CaptureError(f"Pieces must contain 1..{MAX_PIECES} records")
    if value["PieceCount"] != len(pieces):
        raise CaptureError("PieceCount does not match Pieces")
    max_span = 80.1
    prior = None
    for index, piece in enumerate(pieces):
        if not isinstance(piece, dict):
            raise CaptureError(f"piece {index} must be an object")
        exact_keys(piece, PIECE_KEYS, f"piece {index}")
        safe_token(piece["Prefab"], 128, f"piece {index} Prefab")
        safe_token(piece["Category"], 64, f"piece {index} Category")
        for key in ("ItemPrefab", "RuneSchool", "RuneStyle", "TextGlowSchool"):
            safe_token(piece[key], 128 if key == "ItemPrefab" else 32, f"piece {index} {key}", True)
        if not isinstance(piece["SignText"], str) or len(piece["SignText"]) > 1024:
            raise CaptureError(f"piece {index} SignText is invalid")
        if not isinstance(piece["HasSignText"], bool) or not isinstance(piece["HasItemStand"], bool):
            raise CaptureError(f"piece {index} metadata flags must be boolean")
        if not piece["HasItemStand"] and piece["ItemPrefab"]:
            raise CaptureError(f"piece {index} has an item without HasItemStand")
        bounded_int(piece["ItemVariant"], 0, 255, f"piece {index} ItemVariant")
        bounded_int(piece["ItemQuality"], 0, 100, f"piece {index} ItemQuality")
        bounded_int(piece["ItemType"], 0, 255, f"piece {index} ItemType")
        xyz = [finite_number(piece[key], f"piece {index} {key}") for key in ("X", "Y", "Z")]
        if any(item < 0 or item > max_span for item in xyz):
            raise CaptureError(f"piece {index} lies outside the bounded capture span")
        quat = [finite_number(piece[key], f"piece {index} {key}") for key in ("Qx", "Qy", "Qz", "Qw")]
        norm = sum(item * item for item in quat)
        if not 0.90 <= norm <= 1.10:
            raise CaptureError(f"piece {index} has a non-unit rotation")
        current = signature(piece)
        if prior is not None and current < prior:
            raise CaptureError("Pieces are not in deterministic normalized order")
        prior = current
    actual_hash = pieces_hash(pieces)
    if not isinstance(value["PiecesSha256"], str) or value["PiecesSha256"].lower() != actual_hash:
        raise CaptureError("PiecesSha256 does not match the captured records")
    return value


def json_bytes(value: object) -> bytes:
    return (json.dumps(value, indent=2, ensure_ascii=False, separators=(",", ": ")) + "\n").encode("utf-8")


def blueprint_bytes(capture: dict) -> bytes:
    lines = [
        f"#Name:{capture['Name']}",
        "#Creator:ComfyQuestLab capture",
        f"#Description:Deterministic projection of {capture['PiecesSha256']}; metadata is in the .capture.json sidecar.",
        "#Pieces",
    ]
    for piece in capture["Pieces"]:
        fields = [
            piece["Prefab"], piece["Category"],
            number(piece["X"], 4), number(piece["Y"], 4), number(piece["Z"], 4),
            number(piece["Qx"], 6), number(piece["Qy"], 6),
            number(piece["Qz"], 6), number(piece["Qw"], 6), "",
        ]
        lines.append(";".join(fields))
    return ("\n".join(lines) + "\n").encode("utf-8")


def bounds(pieces: list[dict]) -> dict:
    result = {}
    for axis in ("X", "Y", "Z"):
        values = [float(piece[axis]) for piece in pieces]
        result[axis.lower()] = {"min": min(values), "max": max(values), "span": max(values) - min(values)}
    return result


def plan(capture: dict) -> dict:
    return {
        "schema": PLAN_SCHEMA,
        "name": capture["Name"],
        "source_pieces_sha256": capture["PiecesSha256"],
        "selection": capture["Selection"],
        "radius_metres": capture["RadiusMetres"],
        "bounds": bounds(capture["Pieces"]),
        "pieces": [
            {
                "index": index,
                "prefab": piece["Prefab"],
                "category": piece["Category"],
                "position": [piece["X"], piece["Y"], piece["Z"]],
                "rotation": [piece["Qx"], piece["Qy"], piece["Qz"], piece["Qw"]],
                "metadata": {
                    "sign_text": piece["SignText"] if piece["HasSignText"] else None,
                    "item": ({
                        "prefab": piece["ItemPrefab"], "variant": piece["ItemVariant"],
                        "quality": piece["ItemQuality"], "type": piece["ItemType"],
                    } if piece["HasItemStand"] else None),
                    "rune_school": piece["RuneSchool"] or None,
                    "rune_style": piece["RuneStyle"] or None,
                    "text_glow_school": piece["TextGlowSchool"] or None,
                },
            }
            for index, piece in enumerate(capture["Pieces"])
        ],
    }


def preview_bytes(capture: dict) -> bytes:
    pieces = capture["Pieces"]
    extent = bounds(pieces)
    min_x, min_z = extent["x"]["min"], extent["z"]["min"]
    span_x, span_z = max(extent["x"]["span"], 1.0), max(extent["z"]["span"], 1.0)
    scale = min(1080 / span_x, 650 / span_z)
    marks = []
    for piece in pieces:
        x = 60 + (float(piece["X"]) - min_x) * scale
        y = 710 - (float(piece["Z"]) - min_z) * scale
        digest = hashlib.sha256(piece["Category"].encode("utf-8")).hexdigest()
        color = "#" + digest[:6]
        label = html.escape(piece["Prefab"], quote=True)
        shape = "rect" if piece["HasSignText"] else "circle"
        if shape == "rect":
            marks.append(f'<rect x="{x-3:.2f}" y="{y-3:.2f}" width="6" height="6" fill="{color}"><title>{label}</title></rect>')
        else:
            marks.append(f'<circle cx="{x:.2f}" cy="{y:.2f}" r="2.6" fill="{color}"><title>{label}</title></circle>')
    title = html.escape(capture["Name"])
    sha = html.escape(capture["PiecesSha256"][:12])
    svg = f'''<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="800" viewBox="0 0 1200 800">
<rect width="1200" height="800" fill="#08111f"/>
<path d="M60 710H1140M60 60V710" stroke="#26364f" stroke-width="1"/>
<text x="60" y="34" fill="#e8dfcf" font-family="sans-serif" font-size="22">Godbuild: {title}</text>
<text x="1140" y="34" fill="#8fa0bb" font-family="monospace" font-size="13" text-anchor="end">{len(pieces)} pieces · {sha}</text>
<g>{''.join(marks)}</g>
<text x="60" y="755" fill="#8fa0bb" font-family="sans-serif" font-size="13">Top-down X/Z projection · hover a mark for prefab identity</text>
</svg>
'''
    return svg.encode("utf-8")


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def output_files(capture: dict) -> dict[str, bytes]:
    name = capture["Name"]
    capture_data = json_bytes(capture)
    blueprint = blueprint_bytes(capture)
    plan_data = json_bytes(plan(capture))
    preview = preview_bytes(capture)
    preliminary = {
        f"{name}.capture.json": capture_data,
        f"{name}.blueprint": blueprint,
        "plan.json": plan_data,
        "preview.svg": preview,
    }
    counts = Counter(piece["Prefab"] for piece in capture["Pieces"])
    manifest = {
        "schema": MANIFEST_SCHEMA,
        "name": name,
        "source_schema": CAPTURE_SCHEMA,
        "source_pieces_sha256": capture["PiecesSha256"],
        "piece_count": len(capture["Pieces"]),
        "prefab_counts": dict(sorted(counts.items())),
        "bounds": bounds(capture["Pieces"]),
        "metadata": {
            "signs": sum(piece["HasSignText"] for piece in capture["Pieces"]),
            "item_stands": sum(piece["HasItemStand"] for piece in capture["Pieces"]),
            "runes": sum(bool(piece["RuneSchool"] or piece["RuneStyle"]) for piece in capture["Pieces"]),
            "text_glows": sum(bool(piece["TextGlowSchool"]) for piece in capture["Pieces"]),
        },
        "artifacts": {name: {"sha256": sha256(data), "bytes": len(data)} for name, data in preliminary.items()},
        "unsupported": [
            {"surface": "terrain_heights_and_vegetation", "status": "excluded_upstream", "silent": False},
            {"surface": "portal_links", "status": "excluded_upstream", "silent": False},
            {"surface": "container_contents", "status": "excluded_upstream", "silent": False},
            {"surface": "door_state", "status": "excluded_upstream", "silent": False},
            {"surface": "arbitrary_zdo_fields", "status": "excluded_upstream", "silent": False},
            {"surface": "non_unit_scale", "status": "rejected_upstream", "silent": False},
        ],
        "replay": {
            "authority": f"{name}.capture.json + {name}.blueprint",
            "check_before_build": True,
            "post_build_proof": "blueprint_diff must report MATCH",
        },
    }
    preliminary["manifest.json"] = json_bytes(manifest)
    return preliminary


def write_or_check(folder: Path, files: dict[str, bytes], check: bool) -> None:
    if check:
        drift = []
        for name, expected in files.items():
            path = folder / name
            if not path.is_file() or path.read_bytes() != expected:
                drift.append(name)
        if drift:
            raise CaptureError("Godbuild output drift: " + ", ".join(drift))
        return
    folder.mkdir(parents=True, exist_ok=True)
    for name, data in files.items():
        path = folder / name
        temporary = path.with_name(path.name + ".tmp")
        temporary.write_bytes(data)
        os.replace(temporary, path)


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path, help="Lab *.capture.json artifact")
    parser.add_argument("--output-root", type=Path, default=repository_root() / "examples" / "worldbuild")
    parser.add_argument("--check", action="store_true", help="fail if generated artifacts differ")
    parser.add_argument(
        "--derive-architectural-name",
        help="explicitly derive a canonical Lab pair from an architectural-import-candidate",
    )
    parser.add_argument(
        "--derive-yaw-degrees", type=float, default=0.0,
        help="rigid Unity-Y rotation applied only with --derive-architectural-name",
    )
    parser.add_argument(
        "--standalone-bundle-manifest", type=Path,
        help="verified source-less R&D bundle identity; normal checkout use keeps repository verification",
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv or sys.argv[1:])
    root = repository_root()
    try:
        if args.standalone_bundle_manifest:
            assert_standalone_bundle(args.standalone_bundle_manifest)
        else:
            assert_repo_identity(root)
        raw = args.capture.read_bytes()
        if len(raw) > 4 * 1024 * 1024:
            raise CaptureError("capture exceeds 4 MiB")
        value = json.loads(raw.decode("utf-8-sig"))
        if args.derive_architectural_name:
            capture = derive_architectural_capture(
                value, args.derive_architectural_name, args.derive_yaw_degrees
            )
        else:
            if args.derive_yaw_degrees:
                raise CaptureError(
                    "--derive-yaw-degrees requires --derive-architectural-name"
                )
            capture = validate_capture(value)
        folder = args.output_root.resolve() / capture["Name"]
        write_or_check(folder, output_files(capture), args.check)
        verb = "checked" if args.check else (
            "derived" if args.derive_architectural_name else "imported"
        )
        print(f"{verb} {capture['Name']} · {capture['PieceCount']} pieces · {capture['PiecesSha256']}")
        return 0
    except (OSError, UnicodeError, json.JSONDecodeError, CaptureError) as error:
        print(f"capture import rejected: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
