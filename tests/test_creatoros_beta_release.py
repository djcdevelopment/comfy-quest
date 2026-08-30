from __future__ import annotations

import hashlib
import importlib.util
import json
import tempfile
import unittest
import zipfile
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
CONTENT = REPO / "creatoros" / "beta1"
VERIFIER = REPO / "tools" / "release" / "verify_creatoros_beta_release.py"
PLAYER_PACKAGER = REPO / "tools" / "release" / "creatoros_player_package.py"


def load_verifier():
    spec = importlib.util.spec_from_file_location("verify_creatoros_beta_release", VERIFIER)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


def load_player_packager():
    spec = importlib.util.spec_from_file_location("creatoros_player_package", PLAYER_PACKAGER)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


class CreatorOsBetaReleaseTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.verifier = load_verifier()
        cls.player_packager = load_player_packager()

    def build_fixture(self, root: Path) -> Path:
        release = root / "release"
        release.mkdir()
        campaign = json.loads((CONTENT / "campaign.json").read_text(encoding="utf-8"))
        uid = "-7600395338659582326"
        files: list[tuple[str, str, bytes]] = [
            ("server/worlds_local/CreatorOSBeta1.db", "world_db", b"fixture-world-db"),
            ("server/worlds_local/CreatorOSBeta1.fwl", "world_fwl", b"fixture-world-fwl"),
            ("content/venue.json", "venue", (CONTENT / "venue.json").read_bytes()),
            ("content/campaign.json", "campaign", (CONTENT / "campaign.json").read_bytes()),
            (
                "client/payload/BepInEx/config/comfy-quest-runtime/inbox/slayers-signature-hunt-1.0.0.questpack",
                "questpack",
                (CONTENT / "slayers-signature-hunt-1.0.0.questpack").read_bytes(),
            ),
            (
                "client/payload/BepInEx/config/comfy-network-sense/quest-view.json",
                "quest_view",
                (CONTENT / "quest-view.json").read_bytes(),
            ),
            ("client/payload/BepInEx/plugins/ComfyQuestRuntime.dll", "runtime_dll", b"MZruntime"),
            ("client/payload/BepInEx/plugins/ComfyQuestContracts.dll", "contracts_dll", b"MZcontracts"),
            ("client/payload/BepInEx/plugins/Newtonsoft.Json.dll", "newtonsoft_dll", b"MZjson"),
            ("client/payload/BepInEx/plugins/ComfyNetworkSense.dll", "networksense_dll", b"MZnetwork"),
            (
                "client/payload/BepInEx/config/djcdevelopment.valheim.comfyquestruntime.cfg",
                "runtime_config",
                (
                    "[DedicatedPersonalProgression]\n"
                    "Enabled = true\n"
                    f"WorldUid = {uid}\n"
                    f"ContentHash = {campaign['pack']['content_hash']}\n"
                ).encode(),
            ),
            (
                "client/payload/BepInEx/config/djcdevelopment.valheim.comfynetworksense.cfg",
                "networksense_client_config",
                (
                    "[Lumberjacks]\n"
                    "lumberjacksCutoverMode = native\n"
                    "zdoAuthoritativeConsumerEnabled = false\n"
                    "lumberjacksMotionEnabled = false\n"
                    "[LumberjacksGameSession]\n"
                    "lumberjacksGameSessionEnabled = false\n"
                    "[Gameplay]\n"
                    "gameplayEventProducerEnabled = true\n"
                    "questEvaluatorEnabled = true\n"
                    "[Netcode]\n"
                    "zdoRedirectEnabled = false\n"
                    "handshakeResponderEnabled = false\n"
                ).encode(),
            ),
            (
                "server/BepInEx/config/djcdevelopment.valheim.comfynetworksense.cfg",
                "networksense_server_config",
                (
                    "[Lumberjacks]\n"
                    "lumberjacksGatewayUrl = http://gateway:4000\n"
                    "lumberjacksCutoverMode = native\n"
                    "zdoAuthoritativeConsumerEnabled = false\n"
                    "lumberjacksMotionEnabled = false\n"
                    "[LumberjacksGameSession]\n"
                    "lumberjacksGameSessionEnabled = false\n"
                    "[Gameplay]\n"
                    "gameplayEventProducerEnabled = true\n"
                    "questEvaluatorEnabled = true\n"
                    "[Netcode]\n"
                    "zdoRedirectEnabled = false\n"
                    "handshakeResponderEnabled = true\n"
                    "handshakeResponderEndpoint = http://gateway:4000\n"
                    "handshakeResponderStrictMode = true\n"
                    "handshakeResponderWindowId = creatoros-beta1\n"
                    "handshakeResponderActiveSeconds = 0\n"
                ).encode(),
            ),
            (
                "server/BepInEx/config/comfy-network-sense/quest-view.json",
                "server_quest_view",
                (CONTENT / "quest-view.json").read_bytes(),
            ),
            ("creator-kit/studio/Comfy.Quest.Studio.Host.exe", "studio_host", b"MZstudio"),
            ("client/Install-CreatorOsBeta1.ps1", "install_script", b"Write-Output 'install'\n"),
            (
                "evidence/provenance.json",
                "provenance",
                json.dumps(
                    {
                        "schema": "creatoros-beta-provenance/v1",
                        "revision": "a" * 40,
                    },
                    indent=2,
                ).encode()
                + b"\n",
            ),
        ]
        install_files = []
        for relative, _, payload in files:
            if not relative.startswith("client/payload/"):
                continue
            install_files.append(
                {
                    "source": relative.removeprefix("client/"),
                    "target": relative.removeprefix("client/payload/"),
                    "sha256": hashlib.sha256(payload).hexdigest(),
                    "bytes": len(payload),
                }
            )
        files.append(
            (
                "client/install-manifest.json",
                "install_manifest",
                (
                    json.dumps(
                        {
                            "schema": "creatoros-beta-install-manifest/v1",
                            "release_id": "creatoros-beta1",
                            "player_label": "UNASSIGNED",
                            "world_name": "CreatorOSBeta1",
                            "world_uid": uid,
                            "pack_content_hash": campaign["pack"]["content_hash"],
                            "admission_credentials": "out-of-band-one-time-invite",
                            "files": install_files,
                        },
                        indent=2,
                    )
                    + "\n"
                ).encode(),
            )
        )
        artifacts = []
        for relative, role, payload in files:
            path = release / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(payload)
            artifacts.append(
                {
                    "path": relative,
                    "role": role,
                    "sha256": hashlib.sha256(payload).hexdigest(),
                    "bytes": len(payload),
                }
            )
        pair_hash = self.verifier.named_file_hash(
            [
                ("CreatorOSBeta1.db", release / "server/worlds_local/CreatorOSBeta1.db"),
                ("CreatorOSBeta1.fwl", release / "server/worlds_local/CreatorOSBeta1.fwl"),
            ]
        )
        manifest = {
            "schema": "creatoros-beta-release/v1",
            "release_id": "creatoros-beta1",
            "release_state": "frozen",
            "source": {"revision": "a" * 40, "clean": True},
            "world": {"name": "CreatorOSBeta1", "uid": uid, "pair_hash": pair_hash},
            "artifacts": artifacts,
        }
        (release / "release-manifest.json").write_text(
            json.dumps(manifest, indent=2) + "\n", encoding="utf-8"
        )
        (release / "SHA256SUMS").write_text(
            "".join(f"{row['sha256']}  {row['path']}\n" for row in artifacts), encoding="utf-8"
        )
        return release

    def test_complete_release_is_accepted(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            result = self.verifier.verify_release(self.build_fixture(Path(temporary)))
        self.assertEqual("valid", result["status"])
        self.assertEqual(18, result["artifact_count"])

    def test_world_tamper_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            release = self.build_fixture(Path(temporary))
            with (release / "server/worlds_local/CreatorOSBeta1.db").open("ab") as stream:
                stream.write(b"tamper")
            with self.assertRaisesRegex(self.verifier.ReleaseError, "artifact record mismatch"):
                self.verifier.verify_release(release)

    def test_runtime_pin_drift_is_rejected_even_when_file_records_are_rehashed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            release = self.build_fixture(Path(temporary))
            config_path = release / "client/payload/BepInEx/config/djcdevelopment.valheim.comfyquestruntime.cfg"
            config_path.write_text(config_path.read_text().replace("Enabled = true", "Enabled = false"))
            manifest_path = release / "release-manifest.json"
            manifest = json.loads(manifest_path.read_text())
            row = next(value for value in manifest["artifacts"] if value["role"] == "runtime_config")
            payload = config_path.read_bytes()
            row["sha256"] = hashlib.sha256(payload).hexdigest()
            row["bytes"] = len(payload)
            manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
            sums_path = release / "SHA256SUMS"
            sums_path.write_text(
                "".join(f"{value['sha256']}  {value['path']}\n" for value in manifest["artifacts"])
            )
            with self.assertRaisesRegex(self.verifier.ReleaseError, "Enabled pin drifted"):
                self.verifier.verify_release(release)

    def test_player_package_is_deterministic_and_credential_free(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            release = self.build_fixture(root)
            first = root / "first.zip"
            second = root / "second.zip"
            built = self.player_packager.build_package(release, "wave1-a", first)
            self.player_packager.build_package(release, "wave1-a", second)
            verified = self.player_packager.verify_package(first, release_dir=release)
            self.assertEqual(first.read_bytes(), second.read_bytes())
            self.assertEqual("valid", built["status"])
            self.assertEqual("wave1-a", verified["player_label"])
            with zipfile.ZipFile(first) as archive:
                package = json.loads(archive.read("CreatorOSBeta1/player-package.json"))
                install = json.loads(archive.read("CreatorOSBeta1/install-manifest.json"))
            self.assertFalse(package["credentials_included"])
            self.assertEqual("wave1-a", install["player_label"])

    def test_player_package_rejects_a_steam_id_label(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            release = self.build_fixture(Path(temporary))
            with self.assertRaisesRegex(self.player_packager.PackageError, "Steam numeric ID"):
                self.player_packager.build_package(
                    release, "76561198000000042", Path(temporary) / "bad.zip"
                )

    def test_player_package_detects_payload_tamper(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            release = self.build_fixture(root)
            package = root / "player.zip"
            tampered = root / "tampered.zip"
            self.player_packager.build_package(release, "wave1-b", package)
            with zipfile.ZipFile(package) as source, zipfile.ZipFile(tampered, "w") as target:
                for info in source.infolist():
                    payload = source.read(info)
                    if info.filename.endswith("ComfyQuestRuntime.dll"):
                        payload += b"tamper"
                    target.writestr(info, payload)
            with self.assertRaisesRegex(self.player_packager.PackageError, "hash/size mismatch"):
                self.player_packager.verify_package(tampered)


if __name__ == "__main__":
    unittest.main()
