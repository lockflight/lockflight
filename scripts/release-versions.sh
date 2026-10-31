#!/usr/bin/env bash
# The version files a judge release carries agree with the release.
#   scripts/release-versions.sh <version>   the release being cut (release-packages.yml, from the tag)
#   scripts/release-versions.sh             CI: the version plugins/lockflight/.claude-plugin/plugin.json declares. Once the
#                                           release pull request raised it (no tag v<version> on origin yet), the full check
#                                           runs; between releases (the tag exists), only the agreement of the version files.
# The full check: the judge plugin's plugin.json and the Spec Kit preset have the version; the crew's plugin.json has it too
# while the crew is served from the judge's tag (.claude-plugin/marketplace.json), because Claude Code replaces an installed
# plugin only when its version changes; plugins/lockflight/min-engine-version is a version of the same MAJOR.MINOR line, at
# most the release (the plugin calls that line's commands); the crew's min-engine-version is at most the release.
# Run from the repository root. Needs python3; the CI form needs git and network access to origin.
set -euo pipefail

fail() { echo "release-versions: $*" >&2; exit 1; }
[ -f plugins/lockflight/.claude-plugin/plugin.json ] || fail "run from the repository root"
[ $# -le 1 ] || fail "usage: release-versions.sh [<version>]"

mode=release
version=${1:-}
if [ -z "$version" ]; then
  version=$(python3 -I -c 'import json; print(json.load(open("plugins/lockflight/.claude-plugin/plugin.json"))["version"])')
  remote=${LOCKFLIGHT_RELEASE_REMOTE:-origin}
  tags=$(git ls-remote --tags "$remote" "refs/tags/v$version") || fail "cannot list the tags of $remote"
  if [ -n "$tags" ]; then mode=released; fi
fi

python3 -I - "$mode" "$version" <<'PY'
import json, re, sys

mode, version = sys.argv[1], sys.argv[2]
VERSION = re.compile(r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$")
problems = []

def key(value):
    # SemVer 2.0 precedence: a release sorts above its prereleases; numeric identifiers compare as numbers, below text ones.
    core, _, pre = value.partition("-")
    numbers = tuple(int(part) for part in core.split("."))
    if not pre:
        return numbers, 1, ()
    return numbers, 0, tuple((0, int(p), "") if p.isdigit() else (1, 0, p) for p in pre.split("."))

def plugin_version(name):
    return json.load(open(f"plugins/{name}/.claude-plugin/plugin.json", encoding="utf-8")).get("version")

def preset_version():
    for line in open("plugins/lockflight/speckit-preset/preset.yml", encoding="utf-8"):
        match = re.match(r'^  version: *"([^"]*)"', line)
        if match:
            return match.group(1)
    return None

def minimum(name):
    return open(f"plugins/{name}/min-engine-version", encoding="utf-8").read().strip()

if not VERSION.match(version):
    print(f"release-versions: '{version}' is not a SemVer version", file=sys.stderr)
    sys.exit(1)

served = json.load(open(".claude-plugin/marketplace.json", encoding="utf-8"))
crew_ref = next((e["source"].get("ref", "") for e in served["plugins"]
                 if e["name"] == "lockflight-crew" and isinstance(e.get("source"), dict)), "")
crew_follows = not crew_ref.startswith("crew/v")

files = {"plugins/lockflight/.claude-plugin/plugin.json": plugin_version("lockflight"),
         "plugins/lockflight/speckit-preset/preset.yml": preset_version()}
if crew_follows:
    files["plugins/lockflight-crew/.claude-plugin/plugin.json"] = plugin_version("lockflight-crew")
for path, found in files.items():
    if found != version:
        problems.append(f"{path} declares {found}, the release is {version}")

for name in ("lockflight", "lockflight-crew"):
    value = minimum(name)
    path = f"plugins/{name}/min-engine-version"
    if not VERSION.match(value):
        problems.append(f"{path} holds '{value}', not a version")
    elif mode == "release" and (name == "lockflight" or crew_follows):
        if key(value) > key(version):
            problems.append(f"{path} is {value}, above the release {version}")
        if name == "lockflight" and key(value)[0][:2] != key(version)[0][:2]:
            problems.append(f"{path} is {value}: the judge plugin runs the {'.'.join(version.split('.')[:2])} engine's "
                            f"commands, so it names a {'.'.join(version.split('.')[:2])}.x engine")

for problem in problems:
    print(f"release-versions: {problem}", file=sys.stderr)
if problems:
    sys.exit(1)
what = "released; the version files agree" if mode == "released" else "the version files are the release's"
print(f"release-versions: ok, {version} ({what})")
PY
