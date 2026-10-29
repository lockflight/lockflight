#!/usr/bin/env bash
# The complete example in docs/guides/spec-contract.md passes the phase checks it documents: its spec.md passes
# `phase check specify`, its tasks.md passes `phase check tasks` once a person approved the spec, and neither needs Spec Kit.
# Plays the agent (CLAUDECODE=1) and the human (CLAUDECODE unset). Usage: ENGINE=<lockflight binary> spec-contract.sh <workdir>
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
E="${ENGINE:?path to the lockflight binary}"
W="$1"; rm -rf "${W:?}"; mkdir -p "$W"
DOC="$ROOT/docs/guides/spec-contract.md"
ID=001-discount-codes
export CLAUDECODE=1
failures=0
expect() { # expect <exit code> <description> [<text the output must contain>] -- command…
  local want="$1" what="$2" needle=""; shift 2
  if [ "$1" != "--" ]; then needle="$1"; shift; fi
  shift
  local out; out="$("$@" 2>&1)"; local got=$?
  if [ "$got" = "$want" ] && { [ -z "$needle" ] || printf '%s' "$out" | grep -qF -- "$needle"; }; then
    echo "ok   $what"
  else
    echo "FAIL $what (exit $got, want $want${needle:+, output must contain '$needle'})"; echo "$out" | sed 's/^/     /'; failures=$((failures + 1))
  fi
}
# The fenced block right after the line `specs/<id>/<file>`: in the guide, up to the fence that closes it.
example() {
  awk -v name="\`specs/$ID/$1\`" '
    index($0, name) == 1 { want = 1; next }
    want && fence == "" && /^```/ { match($0, /^`+/); fence = substr($0, 1, RLENGTH); next }
    fence != "" && $0 == fence { exit }
    fence != "" { print }
  ' "$DOC"
}
cd "$W" || exit 1; export GIT_AUTHOR_NAME=dev GIT_AUTHOR_EMAIL=dev@example.com GIT_COMMITTER_NAME=dev GIT_COMMITTER_EMAIL=dev@example.com
git init -q; git config user.email dev@example.com; git config user.name dev
printf 'version: 1\n' > lockflight.yaml
mkdir -p "specs/$ID"
for file in spec.md plan.md tasks.md; do
  example "$file" > "specs/$ID/$file"
  [ -s "specs/$ID/$file" ] || { echo "FAIL the guide has no example for $file"; exit 1; }
done
git add -A; git commit -qm example
human() { env -u CLAUDECODE "$E" "$@"; }

[ ! -e .specify ] && echo "ok   the project has no Spec Kit"
expect 0 "the example spec passes phase check specify" "PASS spec.stories: 2 user stories" -- "$E" phase check specify --feature "$ID"
expect 1 "before the approval only spec.approved fails" "FAIL spec.approved" -- "$E" phase check tasks --feature "$ID"
out="$("$E" phase check tasks --feature "$ID" 2>&1)"
if [ "$(printf '%s\n' "$out" | grep -c '^FAIL')" = 1 ]; then echo "ok   no other item fails"; else echo "FAIL other items fail"; echo "$out" | sed 's/^/     /'; failures=$((failures + 1)); fi
expect 0 "a person approves the spec" -- human approve spec --feature "$ID"
expect 0 "the example tasks pass phase check tasks" "PASS tasks.tests-first" -- "$E" phase check tasks --feature "$ID"
expect 0 "a person approves the tasks" -- human approve tasks --feature "$ID"

if [ "$failures" -gt 0 ]; then echo "$failures check(s) failed"; exit 1; fi
echo "all spec contract checks passed"
