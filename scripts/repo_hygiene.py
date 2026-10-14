#!/usr/bin/env python3
"""Read-only index checks and explicit, recoverable cleanup of known build outputs."""
import argparse
import fnmatch
import json
import pathlib
import subprocess
import uuid
from datetime import datetime, timezone

OUTPUTS = ("macos/.build", "macos/.build-app", "macos/build", "macos/DerivedData",
           "webapp/dist", "webapp/build", "webapp/coverage", "windows/publish", "windows/dist")
FORBIDDEN_PARTS = {"node_modules", ".build", ".build-app", "DerivedData", "bin", "obj", "TestResults", "__pycache__"}
FORBIDDEN_SUFFIXES = (".pyc", ".log", ".tmp", ".bak", ".dmg", ".xcresult", ".app", ".pfx", ".p12")

def git(root, *args):
    return subprocess.check_output(["git", "-C", str(root), *args])

def generated(name):
    parts = pathlib.PurePosixPath(name).parts
    return (bool(set(parts) & FORBIDDEN_PARTS) or name.endswith(FORBIDDEN_SUFFIXES)
            or any(name == path or name.startswith(path + "/") for path in OUTPUTS)
            or name.startswith(("release/", "artifacts/")) or ".DS_Store" in parts)

def tracked(root):
    return git(root, "ls-files", "-z").decode().rstrip("\0").split("\0")

def check(root):
    bad = [name for name in tracked(root) if generated(name)]
    if bad:
        raise ValueError("Generated files in Git index:\n" + "\n".join(bad))
    print("Repository index hygiene passed")

def archive(root, destination, apply):
    root = root.resolve()
    names = tracked(root)
    candidates = []
    for relative in OUTPUTS:
        path = root / relative
        if not path.exists() and not path.is_symlink():
            continue
        if any(part.is_symlink() for part in [path, *path.parents] if part == root or root in part.parents):
            raise ValueError(f"Refusing symlink path: {path}")
        if any(name == relative or name.startswith(relative + "/") for name in names):
            raise ValueError(f"Refusing output containing tracked files: {relative}")
        candidates.append((relative, path))
    print(json.dumps({"mode": "archive" if apply else "preview", "paths": [p for p, _ in candidates]}, indent=2))
    if not apply or not candidates:
        return None
    if destination is None:
        raise ValueError("--archive-dir is required with --apply")
    destination = destination.expanduser().resolve()
    if destination == root or root in destination.parents:
        raise ValueError("Archive must be outside the checkout")
    batch = destination / (datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid.uuid4().hex[:8])
    batch.mkdir(parents=True, exist_ok=False)
    manifest = {"source": str(root), "head": git(root, "rev-parse", "HEAD").decode().strip(), "moved": []}
    manifest_path = batch / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2))
    for relative, path in candidates:
        target = batch / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        # Atomic, same-volume rename only. Never copy/delete or follow links.
        path.rename(target)
        manifest["moved"].append(relative)
        manifest_path.write_text(json.dumps(manifest, indent=2))
    print(f"Recoverable archive: {batch}")
    return batch

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["check", "clean"])
    parser.add_argument("--repo", type=pathlib.Path, default=pathlib.Path(__file__).resolve().parents[1])
    parser.add_argument("--archive-dir", type=pathlib.Path)
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    root = pathlib.Path(git(args.repo, "rev-parse", "--show-toplevel").decode().strip()).resolve()
    try:
        if args.command == "check":
            check(root)
        else:
            archive(root, args.archive_dir, args.apply)
    except (ValueError, OSError) as error:
        parser.exit(1, f"{error}\n")

if __name__ == "__main__":
    main()
