#!/usr/bin/env bash
# The published marketplace (.claude-plugin/marketplace.json) serves each plugin from a release tag, not from main, so a merge
# reaches users only through a release. The judge plugin (lockflight) is released with the engine under vX.Y.Z tags; the crew
# (lockflight-crew) under its own crew/vX.Y.Z tags, or from the judge's tag until its first crew release. This script checks
# the pins and moves them.
#   scripts/marketplace-ref.sh [check]          fails unless every pin is coherent (below)
#   scripts/marketplace-ref.sh set vX.Y.Z       points the judge, and a crew still served from a judge tag, at that tag
#   scripts/marketplace-ref.sh set crew/vX.Y.Z  points the crew at that crew tag
# `set` writes each moved entry's tag and commit and copies its description from the tag, then checks.
# A pin is coherent when:
#   - every entry is a git-subdir source on the repository's URL, at plugins/<name>, with a full commit sha; the judge names a
#     vX.Y.Z[-pre] tag, the crew the judge's tag and commit or a crew/vX.Y.Z[-pre] tag;
#   - each ref is a tag that exists on that repository and points at the sha (so a ref is never served before its tag);
#   - at the judge's tag, plugins/lockflight/.claude-plugin/plugin.json has the tag's version and
#     plugins/lockflight/min-engine-version is at most it; nuget.org lists both (a plugin is never served before its engine);
#   - at a crew tag, plugins/lockflight-crew/.claude-plugin/plugin.json has the tag's version, and nuget.org lists the engine
#     plugins/lockflight-crew/min-engine-version names;
#   - the marketplace's name, description and owner are those of the development marketplace
#     (plugins/.claude-plugin/marketplace.json) at the judge's tag, and each plugin's name and description those at its own
#     tag, not on main: users get the tag's plugins, so they read the tag's descriptions;
#   - the development marketplace in the checkout (which scripts/dev-install.sh adds) serves each plugin from the checkout.
# Needs git, python3 and curl, and network access to the repository and nuget.org.
# For tests: LOCKFLIGHT_MARKETPLACE_REMOTE replaces the repository URL for git, LOCKFLIGHT_NUGET_INDEX the nuget.org index URL.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

served=.claude-plugin/marketplace.json
dev=plugins/.claude-plugin/marketplace.json
repository_url=https://github.com/lockflight/lockflight.git
nuget_index=${LOCKFLIGHT_NUGET_INDEX:-https://api.nuget.org/v3-flatcontainer/lockflight.cli/index.json}
judge=lockflight
crew=lockflight-crew

fail() { echo "marketplace-ref: $*" >&2; exit 1; }

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# fetch_tag <remote> <tag>: fetches the tag's commit into $work/repo (a fresh repository each time), as FETCH_HEAD.
fetch_tag() {
  rm -rf "$work/repo"
  git init -q "$work/repo"
  git -C "$work/repo" fetch -q --depth 1 "$1" "refs/tags/$2" || fail "cannot fetch $2 from $1"
}

# at_tag <tag> <path> <out>: writes the file at the fetched tag to <out>, or fails naming it.
at_tag() {
  git -C "$work/repo" show "FETCH_HEAD:$2" > "$3" 2> /dev/null || fail "$1 has no $2"
}

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
VERSION = r"(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?"
JUDGE_TAG = re.compile(rf"^v{VERSION}$")
CREW_TAG = re.compile(rf"^crew/v{VERSION}$")

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

def load(path):
    return json.load(open(path, encoding="utf-8"))

if mode == "pin":
    # pin <served> <dev> <url>: prints "<name> <ref> <sha>" per entry, the judge first.
    served, dev, url = sys.argv[2], sys.argv[3], sys.argv[4]
    market, development = load(served), load(dev)
    pins = {}
    for entry in market["plugins"]:
        name, source = entry["name"], entry.get("source")
        if not isinstance(source, dict) or source.get("source") != "git-subdir":
            die(f"{served}: {name} is not served from a git-subdir source at a release tag")
        if source.get("url") != url or source.get("path") != f"plugins/{name}":
            die(f"{served}: {name} must come from {url} at plugins/{name}")
        ref, sha = source.get("ref", ""), source.get("sha", "")
        if not re.fullmatch(r"[0-9a-f]{40}", sha):
            die(f"{served}: {name} needs the tag's full commit sha")
        pins[name] = (ref, sha)
    if "lockflight" not in pins:
        die(f"{served}: lists no lockflight plugin")
    for name, (ref, sha) in pins.items():
        if name == "lockflight-crew":
            if not (CREW_TAG.match(ref) or (JUDGE_TAG.match(ref) and (ref, sha) == pins["lockflight"])):
                die(f"{served}: {name} names ref '{ref}': a crew tag crew/vX.Y.Z[-pre], or the judge's tag and commit")
        elif not JUDGE_TAG.match(ref):
            die(f"{served}: {name} names ref '{ref}', not a release tag vX.Y.Z[-pre]")
        elif (ref, sha) != pins["lockflight"]:
            die(f"{served}: {name} must name the judge's release tag and commit")
    for entry in development["plugins"]:
        if entry.get("source") != "./" + entry["name"]:
            die(f"{dev}: {entry['name']} must come from ./{entry['name']}, the checkout")
    for name in ["lockflight"] + [name for name in pins if name != "lockflight"]:
        print(name, *pins[name])
elif mode == "marketplace":
    # marketplace <served> <development marketplace at the judge's tag>: the served marketplace's own fields are the tag's.
    served, tagged = sys.argv[2], sys.argv[3]
    same = lambda m: (m["name"], m.get("description"), m.get("owner"))
    if same(load(served)) != same(load(tagged)):
        die(f"{served}: must have the name, description and owner of plugins/.claude-plugin/marketplace.json at the judge's "
            "tag; `set` copies them")
elif mode == "described":
    # described <served> <development marketplace at a tag> <name>: the plugin is listed with the tag's description.
    served, tagged, name = sys.argv[2], sys.argv[3], sys.argv[4]
    find = lambda m: next(((e["name"], e.get("description")) for e in m["plugins"] if e["name"] == name), None)
    if find(load(served)) != find(load(tagged)):
        die(f"{served}: must describe {name} as plugins/.claude-plugin/marketplace.json does at the tag it serves; "
            "`set` copies it")
elif mode == "rewrite":
    # rewrite <served> <development marketplace at the tag> <tag> <sha> <name>…: points those entries at the tag and its
    # commit and copies their descriptions; with the judge among them, also the marketplace's own fields. Two-space JSON.
    served, tagged, ref, sha, names = sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5], set(sys.argv[6:])
    market, development = load(served), load(tagged)
    if "lockflight" in names:
        for field in ("description", "owner"):
            if field in development:
                market[field] = development[field]
    descriptions = {e["name"]: e.get("description") for e in development["plugins"]}
    for entry in market["plugins"]:
        if entry["name"] not in names:
            continue
        if isinstance(entry.get("source"), dict):
            entry["source"]["ref"], entry["source"]["sha"] = ref, sha
        if descriptions.get(entry["name"]) is not None:
            entry["description"] = descriptions[entry["name"]]
    with open(served, "w", encoding="utf-8") as out:
        out.write(json.dumps(market, indent=2, ensure_ascii=False) + "\n")
elif mode == "follows":
    # follows <served>: the plugins a judge tag moves: the judge, and a crew still served from a judge tag.
    market = load(sys.argv[2])
    refs = {e["name"]: e["source"].get("ref", "") for e in market["plugins"] if isinstance(e.get("source"), dict)}
    print(" ".join(n for n, ref in refs.items() if n == "lockflight" or not CREW_TAG.match(ref)))
elif mode == "versions":
    # versions <tag version> <plugin.json>: the plugin has the tag's version.
    version, path = sys.argv[2], sys.argv[3]
    found = load(path).get("version")
    if found != version:
        die(f"at the tag, {path.rsplit('/', 1)[-1].removesuffix('.json')} has version {found}, not {version}")
elif mode == "minimum":
    # minimum <min-engine-version> <release version or -> <label>: a version, and for the judge at most its release.
    minimum, release, label = sys.argv[2], sys.argv[3], sys.argv[4]
    if not JUDGE_TAG.match("v" + minimum):
        die(f"at the tag, {label} '{minimum}' is not a version")
    if release != "-" and key(minimum) > key(release):
        die(f"at the tag, {label} {minimum} is above the release {release}")
elif mode == "nuget":
    # nuget <index.json> <version>…: each version must be listed (nuget.org lowercases them).
    listed = {v.lower() for v in load(sys.argv[2]).get("versions", [])}
    for version in sys.argv[3:]:
        if version.lower() not in listed:
            die(f"nuget.org does not list Lockflight.Cli {version}: publish the release before the marketplace serves it")
PY
}

# check_ref <remote> <name> <ref> <sha>: the tag exists, points at the sha and describes the plugin; the tag stays fetched.
check_ref() {
  local commit
  commit=$(remote_commit "$1" "$3")
  [ -n "$commit" ] || fail "$served names $3 for $2, but $1 has no such tag: tag the release before the marketplace names it"
  [ "$commit" = "$4" ] || fail "$served names $3 at $4 for $2, but the tag points at $commit"
  fetch_tag "$1" "$3"
  at_tag "$3" "$dev" "$work/dev-at-tag.json"
  python_check described "$served" "$work/dev-at-tag.json" "$2" || fail "the description of $2 is not its tag's"
}

# minimum_at <tag> <plugin>: the plugin's min-engine-version at the fetched tag.
minimum_at() {
  local value
  value=$(git -C "$work/repo" show "FETCH_HEAD:plugins/$2/min-engine-version" 2> /dev/null) \
    || fail "$1 has no plugins/$2/min-engine-version"
  printf '%s' "$value" | tr -d '[:space:]'
}

check() {
  local pins remote name ref sha version minimum judge_ref="" crew_ref="" engines=()
  pins=$(python_check pin "$served" "$dev" "$repository_url") || fail "the pin is not coherent"
  remote=${LOCKFLIGHT_MARKETPLACE_REMOTE:-$repository_url}
  while read -r name ref sha; do
    check_ref "$remote" "$name" "$ref" "$sha"
    if [ "$name" = "$judge" ]; then
      judge_ref=$ref
      python_check marketplace "$served" "$work/dev-at-tag.json" || fail "the marketplace's own fields are not the judge tag's"
      version=${ref#v}
      at_tag "$ref" "plugins/$judge/.claude-plugin/plugin.json" "$work/$judge.json"
      python_check versions "$version" "$work/$judge.json" || fail "$ref is not a coherent release"
      minimum=$(minimum_at "$ref" "$judge")
      python_check minimum "$minimum" "$version" min-engine-version || fail "$ref is not a coherent release"
      engines+=("$version" "$minimum")
    elif [ "$name" = "$crew" ] && [ "${ref#crew/v}" != "$ref" ]; then
      crew_ref=$ref
      at_tag "$ref" "plugins/$crew/.claude-plugin/plugin.json" "$work/$crew.json"
      python_check versions "${ref#crew/v}" "$work/$crew.json" || fail "$ref is not a coherent crew release"
      minimum=$(minimum_at "$ref" "$crew")
      python_check minimum "$minimum" - "the crew's min-engine-version" || fail "$ref is not a coherent crew release"
      engines+=("$minimum")
    fi
  done <<< "$pins"

  curl -fsSL --retry 3 -o "$work/index.json" "$nuget_index" || fail "cannot read $nuget_index"
  python_check nuget "$work/index.json" "${engines[@]}" || fail "the engine is not published yet"
  echo "marketplace-ref: ok, the marketplace serves $judge at $judge_ref and $crew at ${crew_ref:-$judge_ref}; nuget.org lists engines ${engines[*]}"
}

# set_pin <tag> <name>…: points those entries at the tag and its commit, with the tag's descriptions.
set_pin() {
  local tag=$1 remote commit
  shift
  remote=${LOCKFLIGHT_MARKETPLACE_REMOTE:-$repository_url}
  commit=$(remote_commit "$remote" "$tag")
  [ -n "$commit" ] || fail "$tag is not a tag on the repository yet: push the tag first"
  fetch_tag "$remote" "$tag"
  at_tag "$tag" "$dev" "$work/dev-at-tag.json"
  python_check rewrite "$served" "$work/dev-at-tag.json" "$tag" "$commit" "$@" || fail "cannot rewrite $served"
}

case "${1:-check}" in
  check) check ;;
  set)
    tag=${2:-}
    if [[ "$tag" =~ ^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]; then
      read -r -a names <<< "$(python_check follows "$served")"
      set_pin "$tag" "${names[@]}"
    elif [[ "$tag" =~ ^crew/v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]; then
      set_pin "$tag" "$crew"
    else
      fail "usage: scripts/marketplace-ref.sh set vX.Y.Z[-pre] | set crew/vX.Y.Z[-pre]"
    fi
    check ;;
  *) fail "usage: scripts/marketplace-ref.sh [check | set vX.Y.Z[-pre] | set crew/vX.Y.Z[-pre]]" ;;
esac
