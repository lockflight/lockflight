#!/usr/bin/env bash
# The incremental Stop gate against the full one on a real build: the ten-project solution of make-graph.sh, a change to a leaf
# project (Api) and to a core one (Domain), each judged by `gate run` (the projects the change affects) and `gate run --full`
# (every project, as the stop profile ran before), interleaved. Before each run the tree is reset and passed with --full, as the
# last Stop left it (built, its tree recorded), and the change is new (so it compiles). Prints each run and the median per case and mode, and checks the choice: Api and
# Api.Tests for the leaf, every project that depends on Domain for the core change.
#   ENGINE=<path to lockflight> bash tests/gates/incremental-timing.sh [runs per case and mode, default 5] [mtp|vstest]
# Needs the .NET 10 SDK and network access to restore the test packages once.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
engine="${ENGINE:?set ENGINE to the lockflight binary}"
runs="${1:-5}"
runner="${2:-mtp}"
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
root="$scratch/graph"
bash "$here/make-graph.sh" "$root" "$runner"
cd "$root"

now_ms() { python3 -c 'import time; print(int(time.time() * 1000))'; }
median() { sort -n | awk '{ v[NR] = $1 } END { print (NR % 2) ? v[(NR + 1) / 2] : int((v[NR / 2] + v[NR / 2 + 1]) / 2) }'; }

: > "$scratch/times"
for run in $(seq 1 "$runs"); do
  for case in leaf core; do
    for mode in incremental full; do
      git checkout -q -- . && git clean -fdq -e bin -e obj -e .lockflight
      # The last Stop's PASS, as the agent's turn starts: everything built, the tree recorded.
      "$engine" gate run --profile stop --full > /dev/null 2>&1 || "$engine" gate run --profile stop --full > /dev/null
      case "$case" in
        leaf) file=src/Api/ApiLayer.cs ;;
        core) file=src/Domain/DomainLayer.cs ;;
      esac
      printf '\n// change %s %s %s\n' "$run" "$mode" "$RANDOM" >> "$file"
      flag=""; [ "$mode" = full ] && flag="--full"
      started="$(now_ms)"
      if ! "$engine" gate run --profile stop $flag > "$scratch/out" 2>&1; then
        # A test host that crashed (exit 139) or a build server hiccup is not a measurement: the same run once more.
        echo "$case $mode: not PASS, run again: $(grep -E '^(build|test): ' "$scratch/out" | tr '\n' ' ')" >&2
        started="$(now_ms)"
        "$engine" gate run --profile stop $flag > "$scratch/out" 2>&1 || { cat "$scratch/out"; exit 1; }
      fi
      elapsed=$(( $(now_ms) - started ))
      steps="$(sed -nE 's/^(build|test): [A-Z/-]+ \(([0-9]+) ms\)$/\2/p' "$scratch/out" | awk '{ sum += $1 } END { print sum + 0 }')"
      chosen="$(grep -A1 '^projects.affected' "$scratch/out" | tail -1 | sed -E 's/^ +//' | cut -c1-60)"
      echo "$case $mode $elapsed $steps" >> "$scratch/times"
      printf '%-5s %-11s %6d ms (build and test %6d ms)  %s\n' "$case" "$mode" "$elapsed" "$steps" "$chosen"
      if [ "$case" = leaf ] && [ "$mode" = incremental ]; then
        grep -q '2 of 10 projects' "$scratch/out" || { echo "the leaf change did not choose Api and Api.Tests" >&2; cat "$scratch/out"; exit 1; }
      fi
      if [ "$case" = core ] && [ "$mode" = incremental ]; then
        grep -q '7 of 10 projects' "$scratch/out" || { echo "the core change did not choose Domain and its 6 dependents" >&2; cat "$scratch/out"; exit 1; }
      fi
    done
  done
done

echo
echo "median of $runs runs ($runner): the whole gate run, and its build and test steps"
for case in leaf core; do
  for mode in incremental full; do
    printf '  %-5s %-11s %6d ms  %6d ms\n' "$case" "$mode" \
      "$(awk -v c="$case" -v m="$mode" '$1 == c && $2 == m { print $3 }' "$scratch/times" | median)" \
      "$(awk -v c="$case" -v m="$mode" '$1 == c && $2 == m { print $4 }' "$scratch/times" | median)"
  done
done
