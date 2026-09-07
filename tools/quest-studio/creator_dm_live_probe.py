#!/usr/bin/env python3
"""Recoverable Linux installation/session boundary for the Creator/DM live lap.

The caller supplies a hash-verified Quest release manifest. Game operations still
use the shipping world-entry, Creator Request, Lab and Studio contracts. The
session's world checkpoint is separate from the original state restored at exit.
"""
from __future__ import annotations

import argparse
import json
import re
import shutil
import socket
from pathlib import Path
from typing import Any

import architectural_live_probe as base

SCHEMA = "comfy-quest-creator-dm-install/v1"
WORLD = "ComfyQuestDemo"
WORLD_UID = "-7600395338659582326"
CHARACTER = "questyfour"
PLUGINS = ("ComfyQuestContracts.dll", "ComfyQuestRuntime.dll", "ComfyQuestLab.dll",
           "Newtonsoft.Json.dll")
CONFIGS = ("comfy-quest-runtime", "comfy-quest-lab", "comfy-quest-creator",
           "djcdevelopment.valheim.comfyquestruntime.cfg",
           "djcdevelopment.valheim.comfyquestlab.cfg")
LOCK = "BepInEx/config/creator-dm-install.lock.json"
SESSION = "BepInEx/config/comfy-quest-creator/session.json"


def tree_hash(path: Path) -> dict[str, str] | None:
    if not path.exists():
        return None
    if path.is_symlink():
        raise RuntimeError("symlink_not_owned")
    if path.is_file():
        return {".": base.sha256(path)}
    result = {}
    for child in sorted(path.rglob("*")):
        if child.is_symlink():
            raise RuntimeError("symlink_not_owned")
        if child.is_file():
            result[child.relative_to(path).as_posix()] = base.sha256(child)
    return result


def contained(root: Path, relative: str) -> Path:
    path = root / relative
    if not relative or Path(relative).is_absolute() or ".." in Path(relative).parts:
        raise RuntimeError("relative_path_invalid")
    if not path.resolve().is_relative_to(root.resolve()) or path.resolve() == root.resolve():
        raise RuntimeError("path_escapes_owned_root")
    return path


class Session:
    def __init__(self, valheim: Path, unity: Path, run: Path, machine: str, session: str):
        self.valheim, self.unity, self.run = map(Path.resolve, (valheim, unity, run))
        base.require_safe_token(machine, "machine")
        base.require_safe_token(session, "session")
        self.machine, self.session = machine, session
        if any(a.is_relative_to(b) for a, b in
               ((self.run, self.valheim), (self.run, self.unity),
                (self.valheim, self.run), (self.unity, self.run))):
            raise RuntimeError("recovery_root_overlaps_install")
        self.state_path = self.run / "install.json"
        self.lock = contained(self.valheim, LOCK)

    def stopped(self) -> None:
        if socket.gethostname().lower() != self.machine.lower():
            raise RuntimeError("machine_identity_mismatch")
        if base.process_snapshot():
            raise RuntimeError("valheim_must_be_stopped")

    def records(self) -> list[dict[str, Any]]:
        paths = [("valheim", "BepInEx/plugins/" + name) for name in PLUGINS]
        paths += [("valheim", "BepInEx/config/" + name) for name in CONFIGS]
        saves = base.matching_save_files(self.unity, WORLD, CHARACTER)
        required = [self.unity / "worlds_local" / (WORLD + suffix) for suffix in (".db", ".fwl")]
        if any(not path.is_file() for path in required):
            raise RuntimeError("canonical_world_pair_missing")
        if not any(path.name == CHARACTER + ".fch" for path in saves):
            raise RuntimeError("character_missing")
        paths += [("unity", path.relative_to(self.unity).as_posix()) for path in saves]
        return [{"root": root, "relative": relative,
                 "directory": self.path(root, relative).is_dir(),
                 "before": tree_hash(self.path(root, relative))} for root, relative in paths]

    def path(self, root: str, relative: str) -> Path:
        if root not in ("valheim", "unity"):
            raise RuntimeError("owned_root_invalid")
        return contained(getattr(self, root), relative)

    def write(self, state: dict) -> None:
        base.atomic_json(self.state_path, state)

    def read(self) -> dict:
        state = base.read_json(self.state_path)
        if (state.get("schema") != SCHEMA or state.get("session_id") != self.session
                or state.get("machine") != self.machine
                or state.get("valheim") != str(self.valheim)
                or state.get("unity") != str(self.unity)):
            raise RuntimeError("recovery_identity_mismatch")
        if state.get("state") != "restored":
            lock = base.read_json(self.lock)
            if lock != {"session_id": self.session, "run_root": str(self.run)}:
                raise RuntimeError("installation_owned_by_another_session")
        return state

    def prepare(self, manifest_path: Path) -> dict:
        self.stopped()
        if self.run.exists() and any(self.run.iterdir()):
            raise RuntimeError("recovery_directory_not_empty")
        if self.lock.exists():
            raise RuntimeError("installation_owned_by_another_session")
        current = contained(self.valheim, SESSION)
        if current.exists() and base.read_json(current).get("state") == "active":
            raise RuntimeError("creator_session_already_active")
        manifest = base.read_json(manifest_path)
        if manifest.get("schema") != "comfy-quest-creator-dm-release/v1":
            raise RuntimeError("release_manifest_invalid")
        if not re.fullmatch(r"[0-9a-f]{40}", manifest.get("source_revision", "")):
            raise RuntimeError("release_source_revision_invalid")
        entries = manifest.get("plugins", [])
        names = [entry.get("name") for entry in entries]
        if sorted(names) != sorted(PLUGINS):
            raise RuntimeError("release_plugin_set_invalid")
        for entry in entries:
            source = contained(manifest_path.resolve().parent, entry["path"])
            if (source.stat().st_size != entry["bytes"]
                    or base.sha256(source) != entry["sha256"]):
                raise RuntimeError("release_artifact_hash_mismatch")
        records = self.records()
        self.run.mkdir(parents=True, exist_ok=True)
        for record in records:
            if record["before"] is None:
                continue
            source = self.path(record["root"], record["relative"])
            target = contained(self.run / "backup" / record["root"], record["relative"])
            target.parent.mkdir(parents=True, exist_ok=True)
            if record["directory"]:
                shutil.copytree(source, target)
            else:
                shutil.copy2(source, target)
            if tree_hash(target) != record["before"]:
                raise RuntimeError("backup_verification_failed")
        state = {"schema": SCHEMA, "state": "preparing", "session_id": self.session,
                 "machine": self.machine, "valheim": str(self.valheim), "unity": str(self.unity),
                 "source_revision": manifest["source_revision"], "records": records,
                 "plugins": entries, "prepared_utc": base.iso(base.utc_now())}
        base.atomic_json(self.lock, {"session_id": self.session, "run_root": str(self.run)}, create=True)
        self.write(state)
        try:
            for entry in entries:
                source = contained(manifest_path.resolve().parent, entry["path"])
                target = contained(self.valheim, "BepInEx/plugins/" + entry["name"])
                base.atomic_copy(source, target)
                if base.sha256(target) != entry["sha256"]:
                    raise RuntimeError("installed_plugin_hash_mismatch")
            config = contained(self.valheim, "BepInEx/config/djcdevelopment.valheim.comfyquestruntime.cfg")
            text = config.read_text(encoding="utf-8-sig") if config.exists() else ""
            if re.search(r"(?m)^PrivateWorldConfirmed\s*=", text):
                text = re.sub(r"(?m)^PrivateWorldConfirmed\s*=.*$", "PrivateWorldConfirmed = true", text)
            else:
                text += "\n[Safety]\nPrivateWorldConfirmed = true\n"
            config.write_text(text, encoding="utf-8")
            state["state"] = "prepared"
            self.write(state)
            return self.checkpoint()
        except Exception:
            self.restore()
            raise

    def checkpoint(self) -> dict:
        """Pin a stopped, saved venue for Studio without replacing the original backup."""
        self.stopped()
        state = self.read()
        if state["state"] != "prepared":
            raise RuntimeError("checkpoint_requires_prepared_session")
        world_backup = []
        for suffix in (".db", ".fwl"):
            source = self.unity / "worlds_local" / (WORLD + suffix)
            backup = self.run / "session-world" / source.name
            backup.parent.mkdir(parents=True, exist_ok=True)
            base.atomic_copy(source, backup)
            if base.sha256(source) != base.sha256(backup):
                raise RuntimeError("world_checkpoint_changed_during_copy")
            world_backup.append({"source": str(source), "backup": str(backup),
                                 "sha256": base.sha256(backup)})
        context = {"schema": "comfy-quest-creator-session/v1", "state": "active",
                   "session_id": self.session, "lane": "4B", "expected_machine": self.machine,
                   "world_uid": WORLD_UID, "world_name": WORLD,
                   "character_profile": CHARACTER, "world_backup": world_backup,
                   "prepared_utc": state["prepared_utc"], "repo_commit": state["source_revision"],
                   "evidence_root": str(self.run), "valheim_root": str(self.valheim),
                   "recovery_schema": SCHEMA, "recovery_manifest": str(self.state_path)}
        base.atomic_json(contained(self.valheim, SESSION), context)
        base.atomic_json(self.run / "creator-session.json", context)
        return {"state": "prepared", "session_id": self.session, "world_backup": world_backup}

    def restore(self) -> dict:
        self.stopped()
        state = self.read()
        if state["state"] == "restored":
            # A crash after persisting restored state may leave our own lease.
            # Never remove a lease acquired by another session in the meantime.
            if self.lock.exists() and base.read_json(self.lock) == {
                    "session_id": self.session, "run_root": str(self.run)}:
                self.lock.unlink()
            return {"state": "restored", "replayed": True}
        # Verify the complete backup before changing any installed byte.
        for record in state["records"]:
            backup = contained(self.run / "backup" / record["root"], record["relative"])
            if tree_hash(backup) != record["before"]:
                raise RuntimeError("recovery_backup_hash_mismatch")
        for record in state["records"]:
            target = self.path(record["root"], record["relative"])
            if target.exists():
                if target.is_dir():
                    shutil.rmtree(target)
                else:
                    target.unlink()
            if record["before"] is not None:
                backup = contained(self.run / "backup" / record["root"], record["relative"])
                target.parent.mkdir(parents=True, exist_ok=True)
                if record["directory"]:
                    shutil.copytree(backup, target)
                else:
                    shutil.copy2(backup, target)
        original_saves = {record["relative"] for record in state["records"] if record["root"] == "unity"}
        for path in base.matching_save_files(self.unity, WORLD, CHARACTER):
            relative = path.relative_to(self.unity).as_posix()
            if relative not in original_saves:
                contained(self.unity, relative).unlink()
        if any(tree_hash(self.path(record["root"], record["relative"])) != record["before"]
               for record in state["records"]):
            raise RuntimeError("restoration_verification_failed")
        state["state"] = "restored"
        state["restored_utc"] = base.iso(base.utc_now())
        self.write(state)
        self.lock.unlink()
        result = {"schema": "comfy-quest-creator-dm-restoration/v1", "state": "restored",
                  "session_id": self.session, "all_original_bytes_restored": True,
                  "records": len(state["records"]), "replayed": False}
        base.atomic_json(self.run / "restoration.json", result)
        return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=("prepare", "checkpoint", "restore", "status"))
    for name in ("valheim-root", "unity-root", "run-root"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--machine", required=True)
    parser.add_argument("--session", required=True)
    parser.add_argument("--manifest", type=Path)
    args = parser.parse_args()
    session = Session(args.valheim_root, args.unity_root, args.run_root, args.machine, args.session)
    if args.operation == "prepare":
        if args.manifest is None:
            parser.error("prepare requires --manifest")
        result = session.prepare(args.manifest)
    elif args.operation == "checkpoint":
        result = session.checkpoint()
    elif args.operation == "restore":
        result = session.restore()
    else:
        state = session.read()
        result = {key: state[key] for key in ("schema", "state", "session_id", "machine", "source_revision")}
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
