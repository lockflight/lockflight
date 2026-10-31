#!/usr/bin/env bash
# scripts/release-versions.sh on hand-made checkouts: a release whose version files are its own passes; a plugin, preset or
# crew version left behind, a judge min-engine-version of another line or above the release fails; a crew on its own tags
# keeps its own version; and without an argument the check is full only once the version has no tag on the remote.
set -euo pipefail

root=$(cd "$(dirname "$0")/../.." && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
failures=0

ok() { echo "ok   $1"; }
bad() { echo "FAIL $1"; failures=$((failures + 1)); }
git_quiet() { git -c user.email=ci@example.com -c user.name=ci -c commit.gpgsign=false -c tag.gpgsign=false "$@"; }

# checkout <dir> <judge> <preset> <crew> <judge min> <crew min> [<crew ref>]: the version files of a repository.
checkout() {
  local dir="$work/$1"
  rm -rf "$dir"
  mkdir -p "$dir/scripts" "$dir/.claude-plugin" "$dir/plugins/lockflight/.claude-plugin" "$dir/plugins/lockflight/speckit-preset" \
    "$dir/plugins/lockflight-crew/.claude-plugin"
  cp "$root/scripts/release-versions.sh" "$dir/scripts/"
  printf '{ "name": "lockflight", "version": "%s" }\n' "$2" > "$dir/plugins/lockflight/.claude-plugin/plugin.json"
  printf 'schema_version: "1.0"\npreset:\n  id: lockflight\n  version: "%s"\n' "$3" > "$dir/plugins/lockflight/speckit-preset/preset.yml"
  printf '{ "name": "lockflight-crew", "version": "%s" }\n' "$4" > "$dir/plugins/lockflight-crew/.claude-plugin/plugin.json"
  printf '%s\n' "$5" > "$dir/plugins/lockflight/min-engine-version"
  printf '%s\n' "$6" > "$dir/plugins/lockflight-crew/min-engine-version"
  printf '{ "plugins": [ { "name": "lockflight", "source": { "source": "git-subdir", "ref": "v0.2.0" } },
    { "name": "lockflight-crew", "source": { "source": "git-subdir", "ref": "%s" } } ] }\n' "${7:-v0.2.0}" > "$dir/.claude-plugin/marketplace.json"
  echo "$dir"
}

run() { local dir="$1"; shift; (cd "$dir" && bash scripts/release-versions.sh "$@") > "$work/out" 2>&1; }
expect_ok() { local label="$1" dir="$2"; shift 2; if run "$dir" "$@"; then ok "$label"; else bad "$label: $(cat "$work/out")"; fi; }
expect_fail() { # expect_fail <label> <text> <dir> <args>…
  local label="$1" text="$2" dir="$3"; shift 3
  if run "$dir" "$@"; then bad "$label (it passed)"; elif grep -qF "$text" "$work/out"; then ok "$label"; else bad "$label: $(cat "$work/out")"; fi
}

expect_ok "a release whose version files are its own" "$(checkout a 0.3.0 0.3.0 0.3.0 0.3.0 0.3.0)" 0.3.0
expect_ok "a patch release may keep the line's older minimum engine" "$(checkout a 0.3.2 0.3.2 0.3.2 0.3.0 0.2.0)" 0.3.2
expect_ok "a prerelease that needs itself" "$(checkout a 0.3.0-rc.1 0.3.0-rc.1 0.3.0-rc.1 0.3.0-rc.1 0.3.0-rc.1)" 0.3.0-rc.1
expect_fail "the judge plugin left at the previous release" "plugins/lockflight/.claude-plugin/plugin.json declares 0.2.0" \
  "$(checkout a 0.2.0 0.3.0 0.3.0 0.3.0 0.3.0)" 0.3.0
expect_fail "the preset left at the previous release" "preset.yml declares 0.2.0" "$(checkout a 0.3.0 0.2.0 0.3.0 0.3.0 0.3.0)" 0.3.0
expect_fail "the crew, served from the judge's tag, left behind" "lockflight-crew/.claude-plugin/plugin.json declares 0.2.0" \
  "$(checkout a 0.3.0 0.3.0 0.2.0 0.3.0 0.3.0)" 0.3.0
expect_ok "a crew on its own tags keeps its version and minimum" "$(checkout a 0.3.0 0.3.0 1.4.0 0.3.0 0.9.0 crew/v1.4.0)" 0.3.0
expect_fail "a judge minimum engine of the previous line" "names a 0.3.x engine" "$(checkout a 0.3.0 0.3.0 0.3.0 0.2.0-alpha.9 0.3.0)" 0.3.0
expect_fail "a judge minimum engine above the release" "above the release 0.3.0-rc.1" \
  "$(checkout a 0.3.0-rc.1 0.3.0-rc.1 0.3.0-rc.1 0.3.0 0.3.0-rc.1)" 0.3.0-rc.1
expect_fail "a crew minimum engine above the release" "lockflight-crew/min-engine-version is 0.4.0, above" \
  "$(checkout a 0.3.0 0.3.0 0.3.0 0.3.0 0.4.0)" 0.3.0
expect_fail "a minimum engine that is not a version" "holds 'next', not a version" "$(checkout a 0.3.0 0.3.0 0.3.0 next 0.3.0)" 0.3.0

dir=$(checkout a 0.3.0 0.3.0 0.3.0 0.3.0 0.3.0) && rm "$dir/plugins/lockflight-crew/min-engine-version"
expect_ok "a crew without a minimum engine" "$dir" 0.3.0
dir=$(checkout a 0.3.0 0.3.0 0.3.0 0.3.0 0.3.0) && rm "$dir/plugins/lockflight/min-engine-version"
expect_fail "a judge plugin without a minimum engine" "plugins/lockflight/min-engine-version is missing" "$dir" 0.3.0

# Without an argument: the version plugin.json declares; full once the remote has no tag for it.
remote="$work/remote"
git init -q --bare "$remote"
seed="$work/seed"
git init -q "$seed" && git_quiet -C "$seed" commit -q --allow-empty -m seed && git_quiet -C "$seed" tag v0.2.0
git_quiet -C "$seed" push -q "$remote" v0.2.0
export LOCKFLIGHT_RELEASE_REMOTE="$remote"
expect_ok "between releases, a minimum engine ahead of the released plugin is allowed" "$(checkout a 0.2.0 0.2.0 0.2.0 0.3.0 0.3.0)"
expect_fail "between releases, the version files still agree" "preset.yml declares 0.1.0, the release is 0.2.0" \
  "$(checkout a 0.2.0 0.1.0 0.2.0 0.3.0 0.3.0)"
expect_ok "raised by the release pull request: the full check passes" "$(checkout a 0.3.0 0.3.0 0.3.0 0.3.0 0.3.0)"
expect_fail "raised by the release pull request: the full check runs" "names a 0.3.x engine" \
  "$(checkout a 0.3.0 0.3.0 0.3.0 0.2.0 0.3.0)"

if [ "$failures" -gt 0 ]; then echo "$failures failure(s)"; exit 1; fi
echo "all release-versions checks passed"
