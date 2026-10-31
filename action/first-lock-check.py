#!/usr/bin/env python3
"""Checks a pull request's first engine lock against the release it names (the CI judge action's first-lock step).

A pull request that adds .lockflight/engine.lock to a base without one (the move from Lockflight 0.2's tool pin) is judged by
the base's 0.2 engine, which has no `engine verify-lock`. The action therefore downloads the GitHub Release's
engine-hashes.json, verifies its attestation with gh, and runs this script, which does what verify-lock does:

  first-lock-check.py <proposed engine.lock> <release engine-hashes.json> <proposed .lockflight/ci/install.sh>

Exit 0 when every released platform's hash and the install script's hash are the ones the release published and the vendored
script is the one the lock pins; exit 1 otherwise. A key that appears twice in either file is refused, because the shell
readers of a lock take the first and a JSON library the last. Run with `python3 -I`.
"""

import hashlib
import json
import re
import sys

RIDS = ("linux-arm64", "linux-x64", "osx-arm64", "osx-x64", "win-x64")
SHA256 = re.compile(r"[0-9a-f]{64}")


def fail(message):
    print(f"engine lock: {message}")
    sys.exit(1)


def unique(pairs):
    seen = {}
    for key, value in pairs:
        if key in seen:
            raise ValueError(f"the key '{key}' appears twice")
        seen[key] = value
    return seen


def load(path, what):
    try:
        with open(path, encoding="utf-8") as stream:
            return json.load(stream, object_pairs_hook=unique)
    except (OSError, ValueError) as error:
        fail(f"the {what} cannot be read: {error}")


def sha256_of(entry):
    value = entry.get("sha256") if isinstance(entry, dict) else None
    return value if isinstance(value, str) and SHA256.fullmatch(value) else None


def main():
    if len(sys.argv) != 4:
        fail("usage: first-lock-check.py <engine.lock> <engine-hashes.json> <install.sh>")
    lock = load(sys.argv[1], "proposed lock")
    release = load(sys.argv[2], "release's engine-hashes.json")
    if lock.get("schema") != "lockflight.engine-lock/v1" or lock.get("channel") != "release":
        fail("the proposed lock is not a lockflight.engine-lock/v1 release lock")
    if release.get("schema") != "lockflight.engine-hashes/v1":
        fail("the release's file is not lockflight.engine-hashes/v1")
    version = lock.get("version")
    if version != release.get("version"):
        fail(f"the lock pins {version}, the release's file is for {release.get('version')}")

    binaries = lock.get("binaries") if isinstance(lock.get("binaries"), dict) else {}
    released = release.get("binaries") if isinstance(release.get("binaries"), dict) else {}
    differing = [rid for rid in RIDS if sha256_of(binaries.get(rid)) is None or sha256_of(binaries.get(rid)) != sha256_of(released.get(rid))]
    if differing:
        fail(f"pins {version}, but its hashes for {', '.join(differing)} are not the ones release v{version} published")

    script = sha256_of(lock.get("installScript"))
    published = sha256_of(release.get("installScript"))
    if published is None:
        fail(f"pins {version}, whose release published no install script hash, so the install script it pins cannot be checked")
    if script != published:
        fail(f"pins {version}, but its install script hash is not the one release v{version} published")
    with open(sys.argv[3], "rb") as stream:
        vendored = hashlib.sha256(stream.read()).hexdigest()
    if vendored != script:
        fail(".lockflight/ci/install.sh is not the script the proposed lock pins")

    commit = str(release.get("commit", ""))[:12]
    print(f"engine lock: the first lock matches the published hashes of release v{version} (commit {commit}), its install script included")


if __name__ == "__main__":
    main()
