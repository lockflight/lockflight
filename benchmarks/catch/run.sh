#!/usr/bin/env bash
# The catch benchmark: scripted pull requests on the samples, each an honest change (a control) or one cheat an agent could
# try, judged twice, by Lockflight's CI judge and by a plain CI (`dotnet test` plus a patch coverage threshold). Each run
# appends one JSON line per pull request to the results file; catch.py summarizes them. Nothing here calls a model.
# Usage: ENGINE=<lockflight binary> run.sh <workdir> [sample…]   (samples: dotnet-single dotnet-mstest dotnet-nunit; default all)
#   ONLY=<regex>       run only the cases whose id matches
#   RESULTS=<file>     the JSON lines file (default <workdir>/results.jsonl)
# The pull requests run one after the other, never in parallel.
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
HERE="$ROOT/benchmarks/catch"
E="${ENGINE:?path to the lockflight binary}"
W="${1:?work directory}"; shift
rm -rf "${W:?}"; mkdir -p "$W"; W="$(cd "$W" && pwd)"
RESULTS="${RESULTS:-$W/results.jsonl}"
SAMPLES=("$@"); [ "${#SAMPLES[@]}" -gt 0 ] || SAMPLES=(dotnet-single dotnet-mstest dotnet-nunit)
PY="$(command -v python3 || command -v python)"
export LOCKFLIGHT_TEMP="$W/temp"; mkdir -p "$LOCKFLIGHT_TEMP"
export GIT_AUTHOR_NAME=dev GIT_AUTHOR_EMAIL=dev@example.com GIT_COMMITTER_NAME=dev GIT_COMMITTER_EMAIL=dev@example.com
# No build server outlives a build, here as in the judge: one build at a time, and nothing left holding memory.
export DOTNET_CLI_USE_MSBUILD_SERVER=0 MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
now_ms() { "$PY" -c 'import time; print(int(time.time() * 1000))'; }

# The plain CI the judge is compared with: `dotnet test` with code coverage on a clean checkout of the head, then the share of
# the changed production lines the tests executed, against the threshold in the head's coverage.json (a file the pull request
# can change, as it can change a codecov.yml). Writes <out>/baseline.json.
baseline() { # baseline <head> <out>
  local head="$1" out="$2" checkout="$2/checkout" status started
  rm -rf "$checkout"; git worktree add -q --detach "$checkout" "$head"
  started=$(now_ms)
  (
    cd "$checkout" || exit 2
    if grep -q '"Microsoft.Testing.Platform"' global.json 2>/dev/null; then
      dotnet test -p:UseSharedCompilation=false --coverage --coverage-output-format cobertura --results-directory "$out/results"
    else
      dotnet test -p:UseSharedCompilation=false --collect "Code Coverage;Format=cobertura" --results-directory "$out/results"
    fi
  ) > "$out/baseline.log" 2>&1
  status=$?
  local elapsed=$(( $(now_ms) - started ))
  dotnet build-server shutdown > /dev/null 2>&1
  "$PY" -I "$HERE/catch.py" baseline --repo "$checkout" --base "$main" --head "$head" --exit-code "$status" \
    --elapsed-ms "$elapsed" --results "$out/results" --threshold-file "$checkout/coverage.json" --out "$out/baseline.json"
  git worktree remove --force "$checkout"
}

# Lockflight's judge as the generated workflow runs it (the review of record not configured), then the report's verdict.
judge() { # judge <head> <out>
  local head="$1" out="$2" started status
  started=$(now_ms)
  "$E" ci --platform github --head "$head" --base "$main" --branch "$FEATURE" --no-review-expected --evidence "$out/evidence" \
    > "$out/judge.log" 2>&1
  status=$?
  local elapsed=$(( $(now_ms) - started ))
  "$E" ci report --evidence "$out/evidence" --head "$head" --out "$out/verdict" > "$out/report.log" 2>&1
  printf '{"exitCode": %s, "elapsedMs": %s}\n' "$status" "$elapsed" > "$out/judge.json"
}

for sample in "${SAMPLES[@]}"; do
  # shellcheck source=benchmarks/catch/samples/dotnet-single.sh
  . "$HERE/samples/$sample.sh"
  repo="$W/$sample/repo"; mkdir -p "$repo"; cd "$repo" || exit 1
  git init -q -b main; git config commit.gpgsign false; git config core.autocrlf false
  fixture_setup
  # The policy both sides start from: Lockflight's version 2 defaults with patch coverage on, and the plain CI's threshold.
  cat > lockflight.yaml <<'YAML'
version: 2
gates:
  coverage:
    collector: microsoft
    patchThreshold: 80
YAML
  printf '{ "patchThreshold": 80 }\n' > coverage.json
  mkdir -p .github; printf '* @shop/maintainers\n' > .github/CODEOWNERS
  printf 'bin/\nobj/\nTestResults/\n.lockflight/ledger/\n.lockflight/local/\n' > .gitignore
  git add -A; git commit -qm base; main=$(git rev-parse HEAD)
  for entry in "${CASES[@]}"; do
    IFS='|' read -r id category kind what <<< "$entry"
    if [ -n "${ONLY:-}" ] && ! [[ "$id" =~ $ONLY ]]; then continue; fi
    echo "== $sample $id ($kind: $what)"
    git checkout -q -B "case/$id" "$main"
    "case_$id"
    head=$(git rev-parse HEAD)
    out="$W/$sample/$id"; mkdir -p "$out"
    judge "$head" "$out"
    baseline "$head" "$out"
    "$PY" -I "$HERE/catch.py" record --sample "$sample" --case "$id" --category "$category" --kind "$kind" --what "$what" \
      --judge "$out/judge.json" --verdict "$out/verdict/verdict.json" --evidence "$out/evidence/evidence.json" \
      --baseline "$out/baseline.json" >> "$RESULTS"
    tail -n 1 "$RESULTS" | "$PY" -I "$HERE/catch.py" line
  done
  git checkout -q main
done
dotnet build-server shutdown > /dev/null 2>&1
echo "results: $RESULTS"
