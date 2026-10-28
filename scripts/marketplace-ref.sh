#!/usr/bin/env bash
# The published marketplace (.claude-plugin/marketplace.json) serves each plugin from a release tag, not from main, so a merge
# reaches users only through a release. This script checks that pin and moves it.
#   scripts/marketplace-ref.sh [check]     fails unless the pin is coherent (below)
#   scripts/marketplace-ref.sh set vX.Y.Z  points every entry at that tag and its commit, then checks
# The pin is coherent when:
#   - every entry is a git-subdir source on the repository's URL, at plugins/<name>, and all name the same ref and sha;
#   - the ref is a release tag that exists on that repository and points at the sha (so a ref is never served before its tag);
#   - at that commit each plugin's plugin.json has the tag's version, and plugins/lockflight/min-engine-version is at most it;
#   - nuget.org lists the tag's engine and the minimum engine (so a plugin is never served before the engine it asks for);
#   - the development marketplace (plugins/.claude-plugin/marketplace.json, which scripts/dev-install.sh adds) lists the same
#     plugins from the checkout.
# Needs git, python3 and curl, and network access to the repository and nuget.org.
# For tests: LOCKFLIGHT_MARKETPLACE_REMOTE replaces the repository URL for git, LOCKFLIGHT_NUGET_INDEX the nuget.org index URL.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

served=.claude-plugin/marketplace.json
dev=plugins/.claude-plugin/marketplace.json
repository_url=https://github.com/lockflight/lockflight.git
nuget_index=${LOCKFLIGHT_NUGET_INDEX:-https://api.nuget.org/v3-flatcontainer/lockflight.cli/index.json}

fail() { echo "marketplace-ref: $*" >&2; exit 1; }

# remote_commit <remote> <tag>: the commit the tag points at on the remote, or nothing when the tag does not exist there.
remote_commit() {
  local lines
  lines=$(git ls-remote --tags "$1" "refs/tags/$2" "refs/tags/$2^{}") || fail "cannot list the tags of $1"
  # An annotated tag lists its tag object and, peeled (^{}), its commit; a lightweight tag lists only the commit.
  printf '%s\n' "$lines" | awk -v peeled="refs/tags/$2^{}" -v plain="refs/tags/$2" \
    '$2 == peeled { p = $1 } $2 == plain { t = $1 } END { print (p != "" ? p : t) }'
}

# The JSON and version rules, in one place. Arguments: <mode> <file>…; prints shell-safe results or fails with a message.
python_check() {
  python3 -I - "$@" <<'PY'
import json, re, sys

mode = sys.argv[1]
TAG = re.compile(r"^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$")

def die(message):
    print(message, file=sys.stderr)
    sys.exit(1)

def key(version):
    # SemVer 2.0 precedence: a release sorts above its prereleases; numeric identifiers compare as numbers, below text ones.
    core, _, pre = version.partition("-")
    numbers = tuple(int(part) for part in core.split("."))
    if not pre:
        return numbers, 1, ()
    return numbers, 0, tuple((0, int(p), "") if p.isdigit() else (1, 0, p) for p in pre.split("."))

if mode == "pin":
    served, dev, url = sys.argv[2], sys.argv[3], sys.argv[4]
    market = json.load(open(served, encoding="utf-8"))
    development = json.load(open(dev, encoding="utf-8"))
    pins = set()
    for entry in market["plugins"]:
        name, source = entry["name"], entry.get("source")
        if not isinstance(source, dict) or source.get("source") != "git-subdir":
            die(f"{served}: {name} is not served from a git-subdir source at a release tag")
        if source.get("url") != url or source.get("path") != f"plugins/{name}":
            die(f"{served}: {name} must come from {url} at plugins/{name}")
        ref, sha = source.get("ref", ""), source.get("sha", "")
        if not TAG.match(ref):
            die(f"{served}: {name} names ref '{ref}', not a release tag vX.Y.Z[-pre]")
        if not re.fullmatch(r"[0-9a-f]{40}", sha):
            die(f"{served}: {name} needs the tag's full commit sha")
        pins.add((ref, sha))
    if len(pins) != 1:
        die(f"{served}: every plugin must name the same release tag and commit")
    same = lambda m: (m["name"], m.get("description"), m.get("owner"))
    listed = lambda m: [(e["name"], e.get("description")) for e in m["plugins"]]
    if same(market) != same(development) or listed(market) != listed(development):
        die(f"{dev}: must list the same marketplace and plugins (names and descriptions) as {served}")
    for entry in development["plugins"]:
        if entry.get("source") != "./" + entry["name"]:
            die(f"{dev}: {entry['name']} must come from ./{entry['name']}, the checkout")
    ref, sha = pins.pop()
    print(ref, sha, " ".join(e["name"] for e in market["plugins"]))
elif mode == "versions":
    # versions <tag version> <min-engine-version> <plugin.json>…
    version, minimum = sys.argv[2], sys.argv[3]
    for path in sys.argv[4:]:
        found = json.load(open(path, encoding="utf-8")).get("version")
        if found != version:
            die(f"at the tag, {path.split('/', 1)[1]} has version {found}, not {version}")
    if not TAG.match("v" + minimum):
        die(f"at the tag, min-engine-version '{minimum}' is not a version")
    if key(minimum) > key(version):
        die(f"at the tag, min-engine-version {minimum} is above the release {version}")
elif mode == "nuget":
    # nuget <index.json> <version>…: each version must be listed (nuget.org lowercases them).
    listed = {v.lower() for v in json.load(open(sys.argv[2], encoding="utf-8")).get("versions", [])}
    for version in sys.argv[3:]:
        if version.lower() not in listed:
            die(f"nuget.org does not list Lockflight.Cli {version}: publish the release before the marketplace serves it")
PY
}

check() {
  local pin ref sha names remote commit version minimum
  pin=$(python_check pin "$served" "$dev" "$repository_url") || fail "the pin is not coherent"
  read -r ref sha names <<< "$pin"
  remote=${LOCKFLIGHT_MARKETPLACE_REMOTE:-$repository_url}
  commit=$(remote_commit "$remote" "$ref")
  [ -n "$commit" ] || fail "$served names $ref, but $remote has no such tag: tag the release before the marketplace names it"
  [ "$commit" = "$sha" ] || fail "$served names $ref at $sha, but the tag points at $commit"

  work=$(mktemp -d)
  trap 'rm -rf "$work"' EXIT
  git init -q "$work/repo"
  git -C "$work/repo" fetch -q --depth 1 "$remote" "refs/tags/$ref" || fail "cannot fetch $ref from $remote"
  version=${ref#v}
  local manifests=()
  for name in $names; do
    git -C "$work/repo" show "FETCH_HEAD:plugins/$name/.claude-plugin/plugin.json" > "$work/$name.json" 2> /dev/null \
      || fail "$ref has no plugins/$name/.claude-plugin/plugin.json"
    manifests+=("$work/$name.json")
  done
  minimum=$(git -C "$work/repo" show "FETCH_HEAD:plugins/lockflight/min-engine-version" 2> /dev/null | tr -d '[:space:]') \
    || fail "$ref has no plugins/lockflight/min-engine-version"
  python_check versions "$version" "$minimum" "${manifests[@]}" || fail "$ref is not a coherent release"

  curl -fsSL --retry 3 -o "$work/index.json" "$nuget_index" || fail "cannot read $nuget_index"
  python_check nuget "$work/index.json" "$version" "$minimum" || fail "the engine is not published yet"
  echo "marketplace-ref: ok, the marketplace serves $names at $ref ($sha), engine $version on nuget.org (plugin needs $minimum or newer)"
}

case "${1:-check}" in
  check) check ;;
  set)
    tag=${2:-}
    [[ "$tag" =~ ^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] || fail "usage: scripts/marketplace-ref.sh set vX.Y.Z[-pre]"
    commit=$(remote_commit "${LOCKFLIGHT_MARKETPLACE_REMOTE:-$repository_url}" "$tag")
    [ -n "$commit" ] || fail "$tag is not a tag on the repository yet: push the tag first"
    # Rewrites only the two values, so the file keeps its layout.
    sed -E -e "s/(\"ref\": *\")[^\"]*\"/\1$tag\"/" -e "s/(\"sha\": *\")[^\"]*\"/\1$commit\"/" "$served" > "$served.tmp"
    mv "$served.tmp" "$served"
    check ;;
  *) fail "usage: scripts/marketplace-ref.sh [check | set vX.Y.Z[-pre]]" ;;
esac
