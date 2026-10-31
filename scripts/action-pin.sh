#!/usr/bin/env bash
# The CI judge's action pin that `lockflight init` writes into generated workflows (Templates.ActionCommit in
# engine/src/Lockflight.Cli/Init/Templates.cs) must hold the action this tree has, or a released engine would generate
# workflows that call an older action than the one it was tested with.
#   scripts/action-pin.sh             fails unless action/ at ActionCommit is action/ at HEAD (CI, on every pull request)
#   scripts/action-pin.sh --release   also fails unless ActionCommit is a commit of HEAD's history (release-packages.yml):
#                                     a pin to a pull request's own commit passes CI, but a release names a commit of main
# --release needs the history back to ActionCommit (fetch-depth: 0). After a change to action/ merges, set ActionCommit to
# that merge commit in a pull request of its own (docs/engineering/releasing.md).
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

fail() { echo "action-pin: $*" >&2; exit 1; }
templates=engine/src/Lockflight.Cli/Init/Templates.cs
commit=$(sed -n 's/^ *public const string ActionCommit = "\([0-9a-f]\{40\}\)";$/\1/p' "$templates")
[ "$(printf '%s' "$commit" | grep -c .)" = 1 ] || fail "$templates must have exactly one ActionCommit of 40 hex digits"
# A pin to a pull request's own commit is not on any branch once the pull request is squash-merged; GitHub still serves it
# by SHA (refs/pull/*/head), so fetch it when a shallow or branch-only clone lacks it.
if ! git cat-file -e "$commit^{commit}" 2> /dev/null; then
  git fetch -q --no-tags --depth=1 origin "$commit" 2> /dev/null || true
fi
git cat-file -e "$commit^{commit}" 2> /dev/null \
  || fail "ActionCommit $commit is neither in this clone nor fetchable from origin: it is not a commit of this repository"
if ! git diff --quiet "$commit" HEAD -- action/; then
  git --no-pager diff --stat "$commit" HEAD -- action/ >&2
  fail "action/ changed since ActionCommit $commit: set ActionCommit in $templates to a commit with this action (after the change merges, its commit on main)"
fi
if [ "${1:-}" = --release ]; then
  git merge-base --is-ancestor "$commit" HEAD \
    || fail "ActionCommit $commit is not in the history of the release commit: pin the action by its commit on main"
fi
echo "action-pin: ok, action/ at ActionCommit $commit is the action at HEAD"
