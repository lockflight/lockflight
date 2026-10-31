#!/usr/bin/env python3
"""Checks a pull request's first engine lock against the release it names (the CI judge action's first-lock step).

A pull request that adds .lockflight/engine.lock to a base without one (the move from Lockflight 0.2's tool pin) is judged by
the base's 0.2 engine, which has no `engine verify-lock`. The action therefore downloads the GitHub Release's
engine-hashes.json, verifies its attestation with gh, and runs this script, which does what verify-lock does:

  first-lock-check.py <proposed engine.lock> <release engine-hashes.json> <proposed .lockflight/ci/install.sh>

Exit 0 when every released platform's hash and the install script's hash are the ones the release published and the vendored
script is the one the lock pins; exit 1 otherwise. The lock is read as strictly as the engine's EngineLockReader and so that
every reader of it agrees: install.sh, which installs from it in CI, reads it with a small awk scanner that does not decode
JSON escapes as JSON does ("\\binaries" is `binaries` to awk, a backspace and `inaries` here). So a lock with any backslash, an
unknown key, or a key that appears twice (the shell readers take the first, a JSON library the last) is refused rather than
read either way. Run with `python3 -I`.
"""

import hashlib
import json
import re
import sys

RIDS = ("linux-arm64", "linux-x64", "osx-arm64", "osx-x64", "win-x64")
LOCK_KEYS = ("schema", "package", "version", "channel", "installScript", "binaries")
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


def load(path, what, escapes_allowed):
    try:
        with open(path, encoding="utf-8") as stream:
            text = stream.read()
        if not escapes_allowed and "\\" in text:
            fail(f"the {what} holds a backslash; a lock names its keys and values without escapes, so every reader reads it alike")
        return json.loads(text, object_pairs_hook=unique)
    except (OSError, ValueError) as error:
        fail(f"the {what} cannot be read: {error}")


def hash_entry(parent, key, where):
    """The hash of an entry { "sha256": "<64 lower-case hex>" }, refusing anything else, as EngineLockReader does."""
    entry = parent.get(key)
    if not isinstance(entry, dict) or set(entry) != {"sha256"} or sha256_of(entry) is None:
        fail(f"'{where}' must be {{ \"sha256\": \"<64 lower-case hex>\" }}")
    return entry["sha256"]


def strict_lock(lock):
    """Refuses a lock the engine would refuse: unknown keys, wrong types, a malformed hash or a missing released platform."""
    if not isinstance(lock, dict):
        fail("the proposed lock must be a JSON object")
    unknown = [key for key in lock if key not in LOCK_KEYS]
    if unknown:
        fail(f"the proposed lock has the unknown key '{unknown[0]}' (known: {', '.join(LOCK_KEYS)})")
    if not isinstance(lock.get("package"), str) or lock["package"].lower() != "lockflight.cli":
        fail("the proposed lock's 'package' must be Lockflight.Cli")
    if not isinstance(lock.get("version"), str) or not re.fullmatch(r"[0-9A-Za-z.-]+", lock["version"]):
        fail("the proposed lock's 'version' must be an engine version without build metadata")
    hash_entry(lock, "installScript", "installScript")
    binaries = lock.get("binaries")
    if not isinstance(binaries, dict):
        fail("'binaries' must map each platform to { \"sha256\": … }")
    for rid in binaries:
        if rid in RIDS:
            hash_entry(binaries, rid, f"binaries.{rid}")
    missing = [rid for rid in RIDS if rid not in binaries]
    if missing:
        fail(f"a release lock needs a hash for every released platform; missing: {', '.join(missing)}")


def sha256_of(entry):
    value = entry.get("sha256") if isinstance(entry, dict) else None
    return value if isinstance(value, str) and SHA256.fullmatch(value) else None


def main():
    if len(sys.argv) != 4:
        fail("usage: first-lock-check.py <engine.lock> <engine-hashes.json> <install.sh>")
    lock = load(sys.argv[1], "proposed lock", escapes_allowed=False)
    release = load(sys.argv[2], "release's engine-hashes.json", escapes_allowed=True)
    if not isinstance(lock, dict) or lock.get("schema") != "lockflight.engine-lock/v1" or lock.get("channel") != "release":
        fail("the proposed lock is not a lockflight.engine-lock/v1 release lock")
    strict_lock(lock)
    if not isinstance(release, dict) or release.get("schema") != "lockflight.engine-hashes/v1":
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
