"""Create and verify deterministic manifests for curated knowledge assets."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
from typing import Final


_LAYOUTS: Final[dict[str, tuple[tuple[Path, Path], ...]]] = {
    "legacy": (
        (Path("roadmaps"), Path("roadmaps")),
        (Path("CBT-Data-md"), Path("cbt/source")),
        (Path("cbt-graph"), Path("cbt/graph")),
    ),
    "canonical": (
        (Path("roadmaps"), Path("roadmaps")),
        (Path("cbt/source"), Path("cbt/source")),
        (Path("cbt/graph"), Path("cbt/graph")),
    ),
}


def build_manifest(root: Path, layout: str) -> dict[str, object]:
    """Hash the source directories into paths independent of their physical root."""
    if layout not in _LAYOUTS:
        raise ValueError(f"Unsupported knowledge layout: {layout}")
    root = root.resolve()
    files: list[dict[str, object]] = []
    for source_relative, logical_relative in _LAYOUTS[layout]:
        source = root / source_relative
        if not source.is_dir():
            raise ValueError(f"Required knowledge directory is missing: {source}")
        for path in sorted(candidate for candidate in source.rglob("*") if candidate.is_file()):
            digest = hashlib.sha256(path.read_bytes()).hexdigest()
            logical_path = (logical_relative / path.relative_to(source)).as_posix()
            files.append(
                {
                    "path": logical_path,
                    "size_bytes": path.stat().st_size,
                    "sha256": digest,
                }
            )
    files.sort(key=lambda item: str(item["path"]))
    return {
        "schema_version": 1,
        "file_count": len(files),
        "total_bytes": sum(int(item["size_bytes"]) for item in files),
        "files": files,
    }


def read_manifest(path: Path) -> dict[str, object]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise ValueError(f"Unable to read manifest {path}: {exc}") from exc
    if not isinstance(value, dict) or not isinstance(value.get("files"), list):
        raise ValueError(f"Invalid manifest format: {path}")
    return value


def write_manifest(path: Path, manifest: dict[str, object]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def verify_manifest(root: Path, layout: str, expected: dict[str, object]) -> None:
    actual = build_manifest(root, layout)
    if actual != expected:
        expected_files = {item["path"]: item for item in expected["files"]}  # type: ignore[index]
        actual_files = {item["path"]: item for item in actual["files"]}  # type: ignore[index]
        missing = sorted(set(expected_files) - set(actual_files))
        unexpected = sorted(set(actual_files) - set(expected_files))
        changed = sorted(
            path for path in set(expected_files) & set(actual_files)
            if expected_files[path] != actual_files[path]
        )
        details = []
        if missing:
            details.append(f"missing={len(missing)}")
        if unexpected:
            details.append(f"unexpected={len(unexpected)}")
        if changed:
            details.append(f"changed={len(changed)}")
        raise ValueError("Knowledge manifest verification failed: " + ", ".join(details))


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Create or verify curated knowledge manifests.")
    subcommands = parser.add_subparsers(dest="command", required=True)
    snapshot = subcommands.add_parser("snapshot")
    snapshot.add_argument("--root", type=Path, required=True)
    snapshot.add_argument("--layout", choices=sorted(_LAYOUTS), required=True)
    snapshot.add_argument("--output", type=Path, required=True)
    verify = subcommands.add_parser("verify")
    verify.add_argument("--root", type=Path, required=True)
    verify.add_argument("--layout", choices=sorted(_LAYOUTS), required=True)
    verify.add_argument("--manifest", type=Path, required=True)
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    if args.command == "snapshot":
        value = build_manifest(args.root, args.layout)
        write_manifest(args.output, value)
        print(f"Wrote manifest: {args.output} ({value['file_count']} files, {value['total_bytes']} bytes)")
        return
    expected = read_manifest(args.manifest)
    verify_manifest(args.root, args.layout, expected)
    print(f"Manifest verified: {args.manifest} ({expected['file_count']} files)")


if __name__ == "__main__":
    main()