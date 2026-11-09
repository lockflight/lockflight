#!/usr/bin/env bash
# The upgrade matrix: a project set up by a published release reaches this engine with one `update --project`. For each
# release with fixtures under schemas/fixtures/released (or each one RELEASES names) and each install level that needs no
# claude CLI (the plugin, the native settings, CI only), with the lite and the standard profile, it runs that release's
# published engine's `init` on samples/dotnet-single and commits it, then this engine's `update --project`, and requires:
#   - `doctor` reports no FAIL;
#   - every file is what a fresh `init` by this engine writes at the same level (the engine lock, the vendored install script,
#     the workflow with its action pin, the hooks and settings, the CLAUDE.md block, the manifest and .gitignore);
#   - a second `update --project` has nothing left to change.
# Then two cases on a project of the newest of those releases: a workflow a person edited is left as it is, with the diff and this engine's version in
# .lockflight/local/migration/; a GitLab pipeline is not migrated (only the lines update has always moved there), and update says
# so.
#
# Usage: ENGINE=<lockflight binary> upgrade-matrix.sh <work dir>
#   ENGINE must carry a release version (dotnet publish -p:Version=99.0.0-ci.1): update never lowers a pin, and only a release
#   writes the committed engine lock. It is served from a file:// feed laid out as nuget.org serves a release, with an
#   engine-hashes.json whose other platforms are stand-ins, so update writes the engine lock as it would for a release.
#   The releases' engines come from nuget.org through scripts/install.sh, or from $RELEASE_ENGINES/<version>/lockflight[.exe].
# Needs git, python3, curl and one of unzip / bsdtar / python3 (install.sh), and the network for the releases.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
E="${ENGINE:?path to the lockflight binary under test}"
W="${1:?work dir}"
# Every release with released fixtures (each release adds its folder), unless RELEASES names some.
RELEASES="${RELEASES:-$(cd "$ROOT/schemas/fixtures/released" && for dir in */; do printf '%s ' "${dir%/}"; done)}"
RELEASES="${RELEASES% }"
LEVELS="plugin native ci-only"
PROFILES="lite standard"
PY="$(command -v python3 || command -v python)"

case "$(uname -s)/$(uname -m)" in
  Darwin/arm64) rid=osx-arm64 exe="" ;;
  Darwin/x86_64) rid=osx-x64 exe="" ;;
  Linux/x86_64) rid=linux-x64 exe="" ;;
  Linux/aarch64) rid=linux-arm64 exe="" ;;
  MINGW*/x86_64|MSYS*/x86_64|CYGWIN*/x86_64) rid=win-x64 exe=.exe ;;
  *) echo "no engine platform for $(uname -s) $(uname -m)" >&2; exit 1 ;;
esac

version=$("$E" version | tr -d '\r')
case "$version" in
  0.0.0-local*|*.dev.*) echo "ENGINE is $version: build it with a release version (dotnet publish -p:Version=99.0.0-ci.1)" >&2; exit 4 ;;
esac
lower=$(printf '%s' "$version" | tr '[:upper:]' '[:lower:]')

fail() { echo "FAIL: $*" >&2; exit 1; }
rm -rf "$W"
mkdir -p "$W/home" "$W/temp"
W="$(cd "$W" && pwd)"
# A person runs update; the engine refuses it inside Claude Code. Nothing reads the developer's home or plugins.
unset CLAUDECODE CLAUDE_CODE_ENTRYPOINT CLAUDE_PLUGIN_ROOT LOCKFLIGHT_NUGET_FEED
export NUGET_PACKAGES="${NUGET_PACKAGES:-$HOME/.nuget/packages}" HOME="$W/home" USERPROFILE="$W/home" LOCKFLIGHT_TEMP="$W/temp"
export GIT_AUTHOR_NAME=dev GIT_AUTHOR_EMAIL=dev@example.com GIT_COMMITTER_NAME=dev GIT_COMMITTER_EMAIL=dev@example.com

# This engine as a release on a file:// feed: its platform's package, and the pointer package with engine-hashes.json.
feed="$W/feed"
hashes="$W/hashes"
mkdir -p "$feed/lockflight.cli.$rid/$lower" "$feed/lockflight.cli/$lower" "$hashes"
package="$feed/lockflight.cli.$rid/$lower/lockflight.cli.$rid.$lower.nupkg"
"$PY" -I -c 'import sys, zipfile
with zipfile.ZipFile(sys.argv[1], "w", zipfile.ZIP_DEFLATED) as archive: archive.write(sys.argv[2], sys.argv[3])' \
  "$package" "$E" "tools/any/$rid/lockflight$exe"
"$PY" -I "$ROOT/scripts/engine-hashes.py" rid --package "$package" --rid "$rid" --version "$version" --out "$hashes" > /dev/null
zeros=$(printf '0%.0s' $(seq 64))
for other in linux-arm64 linux-x64 osx-arm64 osx-x64 win-x64; do
  [ "$other" = "$rid" ] && continue
  name=lockflight; [ "$other" = win-x64 ] && name=lockflight.exe
  printf '{"rid": "%s", "sha256": "%s", "path": "tools/any/%s/%s"}\n' "$other" "$zeros" "$other" "$name" > "$hashes/hash-$other.json"
done
"$PY" -I "$ROOT/scripts/engine-hashes.py" merge --dir "$hashes" --version "$version" --commit "$(printf "0%.0s" $(seq 40))" \
  --install-script "$ROOT/scripts/install.sh" --out "$hashes/engine-hashes.json" > /dev/null
"$PY" -I -c 'import sys, zipfile
with zipfile.ZipFile(sys.argv[1], "w") as archive: archive.write(sys.argv[2], "engine-hashes.json")' \
  "$feed/lockflight.cli/$lower/lockflight.cli.$lower.nupkg" "$hashes/engine-hashes.json"
if [ "$rid" = win-x64 ]; then feed_url="file:///$(cygpath -m "$feed")/"; else feed_url="file://$feed/"; fi

# The releases' published engines.
release_engine() {
  local cached="${RELEASE_ENGINES:-}/$1/lockflight$exe"
  if [ -n "${RELEASE_ENGINES:-}" ] && [ -x "$cached" ]; then printf '%s\n' "$cached"; return; fi
  sh "$ROOT/scripts/install.sh" --version "$1" --dir "$W/releases/$1" 2> "$W/install-$1.log" | tail -n 1 \
    || { cat "$W/install-$1.log" >&2; fail "could not install Lockflight $1"; }
}

# init <engine> <level> <profile> <dir> [extra init options]: the sample, committed, then init with that engine, committed.
init() {
  local engine="$1" level="$2" profile="$3" dir="$4"
  shift 4
  local options=(--profile "$profile" --no-link "$@")
  case "$level" in native) options+=(--native) ;; ci-only) options+=(--ci-only) ;; esac
  mkdir -p "$dir"
  cp -R "$ROOT/samples/dotnet-single/." "$dir"
  (
    cd "$dir"
    rm -rf ./*/*/bin ./*/*/obj
    git init -q -b main
    git config commit.gpgsign false
    git config core.autocrlf false
    git add -A
    git commit -qm "chore: sample"
    # This engine reads its release hashes from the feed above; a release's engine reads its own from nuget.org.
    feed_of=""; [ "$engine" = "$E" ] && feed_of="$feed_url"
    LOCKFLIGHT_NUGET_FEED="$feed_of" "$engine" init "${options[@]}" > "$dir.init.log" 2>&1 || { cat "$dir.init.log" >&2; fail "init by $("$engine" version) in $dir"; }
    git add -A
    git commit -qm "chore: lockflight init"
  )
}

# update <dir>: this engine's update --project, from the release-shaped feed.
update() {
  (cd "$1" && LOCKFLIGHT_NUGET_FEED="$feed_url" "$E" update --project) > "$1.update.log" 2>&1 \
    || { cat "$1.update.log" >&2; fail "update --project in $1"; }
}

# same <updated> <fresh>: the two projects hold the same files, apart from git, build output and the engine's local state.
same() {
  "$PY" -I - "$1" "$2" <<'EOF'
import filecmp, os, sys
skipped = {".git", "bin", "obj"}
local = {".lockflight/bin", ".lockflight/ledger", ".lockflight/local"}
def files(root):
    found = set()
    for directory, children, names in os.walk(root):
        relative = os.path.relpath(directory, root).replace(os.sep, "/")
        children[:] = [c for c in children if c not in skipped and (c if relative == "." else relative + "/" + c) not in local]
        found.update(os.path.relpath(os.path.join(directory, name), root).replace(os.sep, "/") for name in names)
    return found
updated, fresh = files(sys.argv[1]), files(sys.argv[2])
problems = [f"only after the update: {f}" for f in sorted(updated - fresh)] + [f"only in a fresh init: {f}" for f in sorted(fresh - updated)]
problems += [f"differs: {f}" for f in sorted(updated & fresh) if not filecmp.cmp(os.path.join(sys.argv[1], f), os.path.join(sys.argv[2], f), shallow=False)]
print("\n".join(problems))
sys.exit(1 if problems else 0)
EOF
}

no_fail() {
  local status=0
  (cd "$1" && LOCKFLIGHT_NUGET_FEED="$feed_url" "$E" doctor) > "$1.doctor.log" 2>&1 || status=$?
  if grep -q '^FAIL' "$1.doctor.log" || [ "$status" -ne 0 ]; then cat "$1.doctor.log" >&2; fail "doctor after the update of $1 (exit $status)"; fi
}

for profile in $PROFILES; do
  for level in $LEVELS; do
    init "$E" "$level" "$profile" "$W/fresh-$level-$profile"
  done
done

for release in $RELEASES; do
  engine=$(release_engine "$release")
  [ "$("$engine" version | tr -d '\r')" = "$release" ] || fail "$engine is not Lockflight $release"
  for profile in $PROFILES; do
    for level in $LEVELS; do
      project="$W/$release-$level-$profile"
      init "$engine" "$level" "$profile" "$project"
      update "$project"
      same "$project" "$W/fresh-$level-$profile" || { cat "$project.update.log" >&2; fail "$release $level $profile does not match a fresh init after the update"; }
      no_fail "$project"
      update "$project"
      grep -q "already up to date" "$project.update.log" || { cat "$project.update.log" >&2; fail "a second update of $release $level $profile still changed something"; }
      echo "ok: $release, $level level, $profile profile: one update reaches $version"
    done
  done
done

last=$(printf '%s\n' $RELEASES | sort -t . -k 1,1n -k 2,2n -k 3,3n | tail -n 1)
engine=$(release_engine "$last")

# A workflow a person edited is left as it is: update shows the diff and leaves this engine's version beside it.
edited="$W/$last-edited"
init "$engine" plugin lite "$edited"
workflow=.github/workflows/lockflight-gates.yml
printf '\n# Our own note: keep it.\n' >> "$edited/$workflow"
git -C "$edited" commit -qam "ci: our note"
before=$(git -C "$edited" rev-parse "HEAD:$workflow")
update "$edited"
[ "$(git -C "$edited" hash-object "$workflow")" = "$before" ] || fail "update rewrote a workflow a person edited"
grep -q "was edited outside the lines Lockflight writes" "$edited.update.log" || { cat "$edited.update.log" >&2; fail "update did not say it left the edited workflow"; }
cmp -s "$edited/.lockflight/local/migration/$workflow" "$W/fresh-plugin-lite/$workflow" || fail "the version left to merge is not what a fresh init writes"
echo "ok: an edited workflow is left as it is, with this engine's version beside it"

# A GitLab pipeline is not migrated, and update says so.
gitlab="$W/$last-gitlab"
init "$engine" ci-only lite "$gitlab" --ci-platform gitlab
update "$gitlab"
# The job file keeps what Lockflight wrote apart from the line edits update has always made there (the engine install step, the
# policy restore); the root .gitlab-ci.yml, which is the team's file, is not touched.
[ -z "$(git -C "$gitlab" status --porcelain -- .gitlab-ci.yml)" ] || fail "update changed .gitlab-ci.yml"
changed=$(git -C "$gitlab" diff --numstat -- .gitlab/ci/lockflight-gates.yml | awk '{ print $1 + $2 }')
[ "${changed:-0}" -le 10 ] || fail "update rewrote .gitlab/ci/lockflight-gates.yml ($changed lines) instead of leaving it"
grep -q "is not migrated" "$gitlab.update.log" || { cat "$gitlab.update.log" >&2; fail "update did not say the GitLab pipeline is not migrated"; }
echo "ok: a GitLab pipeline is not migrated, and update says so"
echo "upgrade matrix: PASS"
