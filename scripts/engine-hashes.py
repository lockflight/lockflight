#!/usr/bin/env python3
"""Per-platform hashes of the engine executables a release ships (lockflight.engine-hashes/v1).

The hash is of the executable inside each RID package (tools/<tfm>/<rid>/lockflight[.exe]), not of the package: nuget.org
signs every package after the push, which changes the package's bytes but not the executable's. It is also the file
`lockflight link` copies, so the linked engine, a CI install and a lock compare the same bytes.

  engine-hashes.py rid --package <nupkg> --rid <rid> --version <v> --out <dir>
      Extracts the executable to <dir>/lockflight-<v>-<rid>[.exe] and writes <dir>/hash-<rid>.json.
  engine-hashes.py merge --dir <dir> --version <v> --commit <sha> --install-script <install.sh> --out <engine-hashes.json>
      Merges the hash-<rid>.json files (exactly the released platforms, each once) and the hash of the install script the
      release ships, the copy the engine vendors into a project, so a lock's installScript can be checked against the release.
  engine-hashes.py verify --hashes <engine-hashes.json> --packages <dir> [--pointer <nupkg>]
      Re-extracts every RID package and compares it with the file; the pointer package must hold the same file.

Run with `python3 -I`, so nothing beside the script or in the working directory is imported.
"""

import argparse
import hashlib
import json
import os
import re
import sys
import zipfile

SCHEMA = "lockflight.engine-hashes/v1"
PACKAGE = "Lockflight.Cli"
RIDS = ("linux-arm64", "linux-x64", "osx-arm64", "osx-x64", "win-x64")
HASHES_ENTRY = "engine-hashes.json"


def fail(message):
    print(f"engine-hashes: {message}", file=sys.stderr)
    sys.exit(1)


def executable_name(rid):
    return "lockflight.exe" if rid.startswith("win-") else "lockflight"


def entry_of(archive, rid):
    """The one entry tools/<tfm>/<rid>/lockflight[.exe]; never one that leaves the extraction directory."""
    name = re.escape(executable_name(rid))
    pattern = re.compile(rf"^tools/[^/]+/{re.escape(rid)}/{name}$")
    matches = [entry for entry in archive.namelist() if pattern.match(entry)]
    if len(matches) != 1:
        fail(f"{archive.filename}: expected one tools/<tfm>/{rid}/{executable_name(rid)}, found {len(matches)}")
    entry = matches[0]
    if entry.startswith("/") or ".." in entry.split("/"):
        fail(f"{archive.filename}: refusing entry {entry}")
    return entry


def sha256_of(data):
    return hashlib.sha256(data).hexdigest()


def write_json(path, document):
    with open(path, "w", encoding="utf-8", newline="\n") as stream:
        json.dump(document, stream, indent=2)
        stream.write("\n")


def command_rid(arguments):
    if arguments.rid not in RIDS:
        fail(f"unknown RID {arguments.rid} (released: {', '.join(RIDS)})")
    with zipfile.ZipFile(arguments.package) as archive:
        entry = entry_of(archive, arguments.rid)
        data = archive.read(entry)
    os.makedirs(arguments.out, exist_ok=True)
    suffix = ".exe" if arguments.rid.startswith("win-") else ""
    binary = os.path.join(arguments.out, f"lockflight-{arguments.version}-{arguments.rid}{suffix}")
    with open(binary, "wb") as stream:
        stream.write(data)
    digest = sha256_of(data)
    write_json(os.path.join(arguments.out, f"hash-{arguments.rid}.json"), {"rid": arguments.rid, "sha256": digest, "path": entry})
    print(f"{digest}  {arguments.rid}  {entry}")


def command_merge(arguments):
    if not re.fullmatch(r"[0-9a-f]{40}", arguments.commit):
        fail(f"commit must be 40 lower-case hex, got {arguments.commit}")
    binaries = {}
    for name in sorted(os.listdir(arguments.dir)):
        if not (name.startswith("hash-") and name.endswith(".json")):
            continue
        with open(os.path.join(arguments.dir, name), encoding="utf-8") as stream:
            entry = json.load(stream)
        rid = entry.get("rid")
        if rid in binaries:
            fail(f"{rid} is hashed twice")
        if rid not in RIDS or not re.fullmatch(r"[0-9a-f]{64}", entry.get("sha256", "")):
            fail(f"{name} is not a hash of a released platform")
        binaries[rid] = {"sha256": entry["sha256"], "path": entry["path"]}
    if sorted(binaries) != list(RIDS):
        fail(f"expected hashes of {', '.join(RIDS)}; found {', '.join(sorted(binaries)) or 'none'}")
    with open(arguments.install_script, "rb") as stream:
        script = stream.read()
    if b"\r\n" in script:
        fail(f"{arguments.install_script} has CRLF line endings; the script the engine vendors is LF")
    write_json(arguments.out, {
        "schema": SCHEMA,
        "package": PACKAGE,
        "version": arguments.version,
        "commit": arguments.commit,
        "binaries": {rid: binaries[rid] for rid in RIDS},
        "installScript": {"sha256": sha256_of(script)},
    })
    print(f"wrote {arguments.out} for {len(binaries)} platforms")


def command_verify(arguments):
    with open(arguments.hashes, "rb") as stream:
        raw = stream.read()
    document = json.loads(raw)
    if document.get("schema") != SCHEMA or document.get("package") != PACKAGE:
        fail(f"{arguments.hashes} is not {SCHEMA} for {PACKAGE}")
    version = document["version"]
    if sorted(document["binaries"]) != list(RIDS):
        fail(f"{arguments.hashes} does not list exactly {', '.join(RIDS)}")
    if not re.fullmatch(r"[0-9a-f]{64}", document.get("installScript", {}).get("sha256", "")):
        fail(f"{arguments.hashes} has no install script hash")
    for rid in RIDS:
        package = os.path.join(arguments.packages, f"{PACKAGE}.{rid}.{version}.nupkg")
        with zipfile.ZipFile(package) as archive:
            entry = entry_of(archive, rid)
            actual = sha256_of(archive.read(entry))
        expected = document["binaries"][rid]["sha256"]
        if actual != expected or entry != document["binaries"][rid]["path"]:
            fail(f"{package}: {entry} is {actual}, {arguments.hashes} says {expected}")
        print(f"ok {rid} {actual}")
    if arguments.pointer:
        with zipfile.ZipFile(arguments.pointer) as archive:
            if HASHES_ENTRY not in archive.namelist():
                fail(f"{arguments.pointer} holds no {HASHES_ENTRY}")
            if archive.read(HASHES_ENTRY) != raw:
                fail(f"{arguments.pointer}: its {HASHES_ENTRY} differs from {arguments.hashes}")
        print(f"ok pointer {arguments.pointer}")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)
    rid = commands.add_parser("rid")
    rid.add_argument("--package", required=True)
    rid.add_argument("--rid", required=True)
    rid.add_argument("--version", required=True)
    rid.add_argument("--out", required=True)
    merge = commands.add_parser("merge")
    merge.add_argument("--dir", required=True)
    merge.add_argument("--version", required=True)
    merge.add_argument("--commit", required=True)
    merge.add_argument("--install-script", required=True)
    merge.add_argument("--out", required=True)
    verify = commands.add_parser("verify")
    verify.add_argument("--hashes", required=True)
    verify.add_argument("--packages", required=True)
    verify.add_argument("--pointer")
    arguments = parser.parse_args()
    {"rid": command_rid, "merge": command_merge, "verify": command_verify}[arguments.command](arguments)


if __name__ == "__main__":
    main()
