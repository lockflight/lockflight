"""Checks that every copy of the gitleaks pin agrees: one version, and one SHA-256 per platform archive. The copies are the CI
judge's action, this repository's install action and release workflow, and the templates the engine writes (GitHub workflow and
the other CI platforms' scripts, Linux x64 only). Usage: python3 -I scripts/gitleaks-pins.py (from the repository root)."""
import re
import sys
from pathlib import Path

HEX = r"([0-9a-f]{64})"


def read(path):
    return Path(path).read_text(encoding="utf-8")


def platform(name):
    return name.lower().replace("-", "_")


copies = {}  # file -> (versions, {platform: sha})

action = read("action/action.yml")
copies["action/action.yml"] = (set(re.findall(r"GITLEAKS_VERSION: ([0-9.]+)", action)),
                               {platform(p): h for p, h in re.findall(r"GITLEAKS_SHA256_([A-Z0-9_]+): " + HEX, action)})
install = read(".github/actions/install-gitleaks/action.yml")
copies[".github/actions/install-gitleaks/action.yml"] = (
    set(re.findall(r"GITLEAKS_VERSION: ([0-9.]+)", install)),
    {platform(p): h for p, h in re.findall(r"GITLEAKS_SHA256_([A-Z0-9_]+): " + HEX, install)})
release = read(".github/workflows/release-packages.yml")
copies[".github/workflows/release-packages.yml"] = (
    set(re.findall(r"GITLEAKS_VERSION: ([0-9.]+)", release)),
    {platform(p): h for p, h in re.findall(r"archive=([a-z0-9_]+)\.(?:tar\.gz|zip); sum=" + HEX, release)})
scripts = read("engine/src/Lockflight.Cli/Init/CiShellScripts.cs")
copies["engine/src/Lockflight.Cli/Init/CiShellScripts.cs"] = (
    set(re.findall(r'GitleaksVersion = "([0-9.]+)"', scripts)),
    {"linux_x64": h for h in re.findall(r'GitleaksSha256 = "' + HEX + '"', scripts)})
templates = read("engine/src/Lockflight.Cli/Init/Templates.cs")
copies["engine/src/Lockflight.Cli/Init/Templates.cs"] = (
    set(re.findall(r"GITLEAKS_VERSION: ([0-9.]+)", templates)),
    {"linux_x64": h for h in re.findall(r"GITLEAKS_SHA256: " + HEX, templates)})

problems = []
reference_versions, reference = copies["action/action.yml"]
if len(reference_versions) != 1 or not reference:
    problems.append("action/action.yml: expected one GITLEAKS_VERSION and GITLEAKS_SHA256_<PLATFORM> entries")
version = next(iter(reference_versions), None)
for path, (versions, sums) in copies.items():
    if versions != {version}:
        problems.append(f"{path}: gitleaks version {sorted(versions) or 'missing'}, action/action.yml has {version}")
    if not sums:
        problems.append(f"{path}: no gitleaks SHA-256 found")
    for name, sha in sums.items():
        if reference.get(name) != sha:
            problems.append(f"{path}: the {name} SHA-256 is {sha}, action/action.yml has {reference.get(name, 'none')}")
for problem in problems:
    print(f"gitleaks-pins: {problem}", file=sys.stderr)
if problems:
    sys.exit(1)
print(f"gitleaks-pins: ok, gitleaks {version} with the same SHA-256 per platform in {len(copies)} files")
