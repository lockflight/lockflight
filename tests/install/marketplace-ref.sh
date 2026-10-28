#!/usr/bin/env bash
# scripts/marketplace-ref.sh against a local repository and a local package index: `set` pins the marketplace to a release tag
# that is coherent, and `check` refuses a tag that does not exist, a sha that is not the tag's, a plugin version that is not
# the tag's, a minimum engine above the release, an engine not yet published and a development marketplace that drifted.
# Usage: bash tests/install/marketplace-ref.sh        (macOS, Linux; needs git, python3, curl)
set -u
REPO=$(cd "$(dirname "$0")/../.." && pwd)
WORK=$(mktemp -d); pass=0; fail=0
trap 'rm -rf "$WORK"' EXIT
ok() { pass=$((pass+1)); echo "ok   $1"; }
bad() { fail=$((fail+1)); echo "FAIL $1"; }
git_quiet() { git -c user.email=ci@example.com -c user.name=ci -c commit.gpgsign=false -c tag.gpgsign=false "$@"; }

# A stand-in for the repository: a release commit with both plugins at <version> and the minimum engine <minimum>.
remote="$WORK/remote"
git init -q "$remote"
release() { # release <tag> <plugin version> <min-engine-version>
  for name in lockflight lockflight-crew; do
    mkdir -p "$remote/plugins/$name/.claude-plugin"
    printf '{ "name": "%s", "version": "%s" }\n' "$name" "$2" > "$remote/plugins/$name/.claude-plugin/plugin.json"
  done
  printf '%s\n' "$3" > "$remote/plugins/lockflight/min-engine-version"
  git_quiet -C "$remote" add -A && git_quiet -C "$remote" commit -q --allow-empty -m "$1" && git_quiet -C "$remote" tag -a "$1" -m "$1"
}
release v1.0.0 1.0.0 1.0.0
release v1.1.0 1.0.0 1.0.0      # tagged without raising the plugin versions
release v1.2.0 1.2.0 1.3.0      # asks for an engine newer than itself
release v1.4.0-alpha.2 1.4.0-alpha.2 1.4.0-alpha.10
release v1.4.0-alpha.10 1.4.0-alpha.10 1.4.0-alpha.2

# The checkout: the script and both marketplace files from this repository.
checkout="$WORK/checkout"
mkdir -p "$checkout/scripts" "$checkout/.claude-plugin" "$checkout/plugins/.claude-plugin"
cp "$REPO/scripts/marketplace-ref.sh" "$checkout/scripts/"
cp "$REPO/.claude-plugin/marketplace.json" "$checkout/.claude-plugin/"
cp "$REPO/plugins/.claude-plugin/marketplace.json" "$checkout/plugins/.claude-plugin/"
git init -q "$checkout"
printf '{ "versions": ["1.0.0", "1.1.0", "1.2.0", "1.3.0", "1.4.0-alpha.2", "1.4.0-ALPHA.10"] }\n' > "$WORK/index.json"
export LOCKFLIGHT_MARKETPLACE_REMOTE="file://$remote" LOCKFLIGHT_NUGET_INDEX="file://$WORK/index.json"

run() { (cd "$checkout" && bash scripts/marketplace-ref.sh "$@") > "$WORK/out" 2>&1; }
expect_ok() { if run "${@:2}"; then ok "$1"; else bad "$1: $(cat "$WORK/out")"; fi; }
expect_fail() { # expect_fail <what> <text the message must contain> <args>…
  if run "${@:3}"; then bad "$1: passed"; elif grep -qF "$2" "$WORK/out"; then ok "$1"; else bad "$1: $(cat "$WORK/out")"; fi
}
served="$checkout/.claude-plugin/marketplace.json"

expect_ok "set pins a coherent release" set v1.0.0
sha=$(git -C "$remote" rev-parse 'v1.0.0^{commit}')
if [ "$(grep -c "\"ref\": \"v1.0.0\"" "$served")" = 2 ] && [ "$(grep -c "\"sha\": \"$sha\"" "$served")" = 2 ]; then
  ok "set writes the tag and its commit into both entries"
else
  bad "set wrote: $(cat "$served")"
fi
expect_ok "check accepts the pin" check
expect_fail "set refuses a tag that does not exist yet" "not a tag on the repository yet" set v9.9.9
sed -i.bak 's/"ref": "v1.0.0"/"ref": "v9.9.9"/' "$served"
expect_fail "check refuses a ref to a missing tag" "has no such tag" check
sed -i.bak "s/\"ref\": \"v9.9.9\"/\"ref\": \"v1.0.0\"/; s/$sha/$(git -C "$remote" rev-parse 'v1.1.0^{commit}')/" "$served"
expect_fail "check refuses a sha that is not the tag's" "but the tag points at" check
expect_fail "check refuses a tag whose plugins have another version" "has version 1.0.0, not 1.1.0" set v1.1.0
expect_fail "check refuses a minimum engine above the release" "min-engine-version 1.3.0 is above" set v1.2.0
expect_fail "prerelease numbers compare as numbers (alpha.10 is above alpha.2)" "is above the release 1.4.0-alpha.2" set v1.4.0-alpha.2
expect_ok "a prerelease that needs an older prerelease, listed in another case on nuget.org" set v1.4.0-alpha.10
printf '{ "versions": ["1.1.0"] }\n' > "$WORK/index.json"
expect_fail "check refuses a release nuget.org does not list" "nuget.org does not list Lockflight.Cli 1.0.0" set v1.0.0
printf '{ "versions": ["1.0.0"] }\n' > "$WORK/index.json"
expect_ok "the release is coherent again" set v1.0.0
sed -i.bak 's#"./lockflight-crew"#"./crew"#' "$checkout/plugins/.claude-plugin/marketplace.json"
expect_fail "check refuses a development marketplace that drifted" "must come from ./lockflight-crew" check

echo "$pass passed, $fail failed"
[ "$fail" = 0 ]
