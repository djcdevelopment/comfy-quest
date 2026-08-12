#!/usr/bin/env python3
"""Wait for one exact NuGet.org package version and download it atomically."""

from __future__ import annotations

import argparse
import time
import urllib.error
import urllib.request
from pathlib import Path


def package_url(package_id: str, version: str) -> str:
    lowered_id = package_id.lower()
    lowered_version = version.lower()
    return (
        "https://api.nuget.org/v3-flatcontainer/"
        f"{lowered_id}/{lowered_version}/{lowered_id}.{lowered_version}.nupkg"
    )


def download(package_id: str, version: str, output: Path, timeout: int) -> None:
    url = package_url(package_id, version)
    deadline = time.monotonic() + timeout
    attempts = 0
    last_error = "not attempted"
    while time.monotonic() <= deadline:
        attempts += 1
        try:
            request = urllib.request.Request(
                url,
                headers={"User-Agent": "comfy-quest-publication-gate/1"},
            )
            with urllib.request.urlopen(request, timeout=30) as response:
                if response.status != 200:
                    raise RuntimeError(f"HTTP {response.status}")
                payload = response.read()
            if len(payload) < 100:
                raise RuntimeError("downloaded package is implausibly small")
            output.parent.mkdir(parents=True, exist_ok=True)
            temporary = output.with_name(output.name + ".downloading")
            temporary.write_bytes(payload)
            temporary.replace(output)
            print(
                f"AVAILABLE id={package_id} version={version} "
                f"bytes={len(payload)} attempts={attempts}"
            )
            return
        except (OSError, RuntimeError, urllib.error.URLError) as exc:
            last_error = str(exc)
            if time.monotonic() > deadline:
                break
            print(
                f"waiting for {package_id} {version}: attempt {attempts}: "
                f"{last_error}",
                flush=True,
            )
            time.sleep(min(15, max(1, deadline - time.monotonic())))
    raise RuntimeError(
        f"NuGet.org did not serve {package_id} {version} within {timeout}s; "
        f"last error: {last_error}"
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--id", required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--timeout", type=int, default=900)
    args = parser.parse_args()
    if args.timeout < 1 or args.timeout > 3600:
        parser.error("--timeout must be between 1 and 3600 seconds")
    download(args.id, args.version, args.out, args.timeout)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
