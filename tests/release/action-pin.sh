#!/usr/bin/env bash
# scripts/action-pin.sh on a scratch repository: a pin to the commit that last changed action/ passes; a later change to
# action/ fails until the pin moves; a pin to a commit outside HEAD's history with the same action passes CI and fails
# --release; a pin missing from the clone fails.
set -euo pipefail

root=$(cd "$(dirname "$0")/../.." && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
failures=0

ok() { echo "ok   $1"; }
bad() { echo "FAIL $1"; failures=$((failures + 1)); }
git_quiet() { git -c user.email=ci@example.com -c user.name=ci -c commit.gpgsign=false "$@"; }

repo="$work/repo"
templates="$repo/engine/src/Lockflight.Cli/Init/Templates.cs"
mkdir -p "$repo/scripts" "$repo/action" "$(dirname "$templates")"
cp "$root/scripts/action-pin.sh" "$repo/scripts/"
git init -q -b main "$repo"
commit() { git_quiet -C "$repo" add -A && git_quiet -C "$repo" commit -q -m "$1" && git -C "$repo" rev-parse HEAD; }
pin() { printf '    public const string ActionCommit = "%s";\n' "$1" > "$templates"; }

run() { (cd "$repo" && bash scripts/action-pin.sh "$@") > "$work/out" 2>&1; }
expect_ok() { local label="$1"; shift; if run "$@"; then ok "$label"; else bad "$label: $(cat "$work/out")"; fi; }
expect_fail() { # expect_fail <label> <text> <args>…
  local label="$1" text="$2"; shift 2
  if run "$@"; then bad "$label (it passed)"; elif grep -qF "$text" "$work/out"; then ok "$label"; else bad "$label: $(cat "$work/out")"; fi
}

echo "v1" > "$repo/action/action.yml"
pin 0000000000000000000000000000000000000000
first=$(commit "the action")
pin "$first"
commit "pin the action" > /dev/null
expect_ok "a pin to the commit that last changed action/" --release
echo "other" > "$repo/README.md"
commit "a change elsewhere" > /dev/null
expect_ok "changes outside action/ keep the pin" --release

echo "v2" > "$repo/action/action.yml"
second=$(commit "change the action")
expect_fail "a change to action/ fails until the pin moves" "action/ changed since ActionCommit $first"
pin "$second"
commit "pin the changed action" > /dev/null
expect_ok "the moved pin passes" --release

# A pull request pinning its own commit: same action, not in main's history once squashed.
git_quiet -C "$repo" checkout -q -b pr "$second~1"
echo "v3" > "$repo/action/action.yml"
own=$(commit "change the action again")
git_quiet -C "$repo" checkout -q main
echo "v3" > "$repo/action/action.yml"
commit "the squashed change" > /dev/null
pin "$own"
commit "pin the pull request's commit" > /dev/null
expect_ok "CI accepts a pin with this action from another commit"
expect_fail "the release refuses a pin outside its history" "not in the history of the release commit" --release

pin 1111111111111111111111111111111111111111
commit "pin a missing commit" > /dev/null
expect_fail "a pin missing from the clone and origin fails" "neither in this clone nor fetchable"
printf '    public const string ActionCommit = "main";\n' > "$templates"
expect_fail "a pin that is not a SHA fails" "exactly one ActionCommit of 40 hex digits"

# A squash-merged pull request's own commit: on no branch, served by SHA from refs/pull/<n>/head (as GitHub does).
remote="$work/remote.git"
git init -q --bare "$remote"
git -C "$remote" config uploadpack.allowReachableSHA1InWant true
git_quiet -C "$repo" push -q "$remote" main
other="$work/other"
git clone -q -b main "$remote" "$other"
echo "v4" > "$other/action/action.yml"
git_quiet -C "$other" commit -q -am "the pull request's action change"
pull=$(git -C "$other" rev-parse HEAD)
git_quiet -C "$other" push -q origin "HEAD:refs/pull/1/head"
git -C "$repo" remote add origin "$remote"
echo "v4" > "$repo/action/action.yml"
pin "$pull"
commit "the squashed change, pinned to the pull request's commit" > /dev/null
expect_ok "a pin served only from a pull request ref is fetched by SHA"

if [ "$failures" -gt 0 ]; then echo "$failures failure(s)"; exit 1; fi
echo "all action-pin checks passed"
