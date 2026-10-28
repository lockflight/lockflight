#!/usr/bin/env bash
# Hook latency: the PreToolUse decision runs before every tool call, so its cost is measured, not assumed.
#   tests/benchmark/hook-latency.sh <engine> [<other engine>]
# With one engine, prints p50 and p95 per case. With two (e.g. the build of main and the build of a branch), runs them
# interleaved, call by call, on the same fixture and prints both, the p50 difference with a 95% bootstrap confidence interval
# and the p95 difference, so machine noise lands on both sides alike. RUNS (default 1000) calls per engine and case, after a
# warm-up of WARMUP (default 50). Needs python3. Exits non-zero only when a call's exit code is not the case's expected one.
set -euo pipefail

if [ "$#" -lt 1 ] || [ "$#" -gt 2 ]; then
  echo "usage: $0 <engine> [<other engine>]" >&2
  exit 4
fi

fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT

project="$fixture/project"
mkdir -p "$project/src" "$project/.claude/worktrees"
printf 'version: 1\n' > "$project/lockflight.yaml"
printf 'class A {}\n' > "$project/src/A.cs"
for name in alpha beta gamma; do
  mkdir -p "$project/.claude/worktrees/$name/src"
  printf 'gitdir: %s\n' "$project/.git/worktrees/$name" > "$project/.claude/worktrees/$name/.git"
  printf 'version: 1\n' > "$project/.claude/worktrees/$name/lockflight.yaml"
done

# The worktree case runs on its own copy, so the other cases measure a project without worktrees.
plain="$fixture/plain"
mkdir -p "$plain/src"
printf 'version: 1\n' > "$plain/lockflight.yaml"
printf 'class A {}\n' > "$plain/src/A.cs"

RUNS="${RUNS:-1000}" WARMUP="${WARMUP:-50}" python3 - "$plain" "$project" "$@" <<'PY'
import json, os, random, statistics, subprocess, sys, time

plain, worktrees, engines = sys.argv[1], sys.argv[2], sys.argv[3:]
runs, warmup = int(os.environ["RUNS"]), int(os.environ["WARMUP"])

def edit(root, path):
    return json.dumps({"hook_event_name": "PreToolUse", "cwd": root, "tool_name": "Edit",
                       "tool_input": {"file_path": os.path.join(root, path), "old_string": "a", "new_string": "b"}})

def bash(root, command):
    return json.dumps({"hook_event_name": "PreToolUse", "cwd": root, "tool_name": "Bash", "tool_input": {"command": command}})

# (name, project, payload, expected exit code); None runs /usr/bin/true, the cost of starting any process.
cases = [
    ("process floor (/usr/bin/true)", plain, None, 0),
    ("Edit of a source file (allow)", plain, edit(plain, "src/A.cs"), 0),
    ("Edit of lockflight.yaml (block, ledger write)", plain, edit(plain, "lockflight.yaml"), 2),
    ("Bash redirect and pipe into protected files (block)", plain, bash(plain, "echo x > global.json && cat src/A.cs | sort > out.txt"), 2),
    ("Edit of a source file, three agent worktrees (allow)", worktrees, edit(worktrees, "src/A.cs"), 0),
]

def call(engine, root, payload, expected):
    argv = ["/usr/bin/true"] if payload is None else [engine, "hook", "pre-tool-use"]
    env = dict(os.environ, CLAUDE_PROJECT_DIR=root)
    started = time.perf_counter_ns()
    result = subprocess.run(argv, input=(payload or "").encode(), stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, env=env, cwd=root)
    elapsed = (time.perf_counter_ns() - started) / 1e6
    if result.returncode != expected:
        sys.exit(f"{engine}: exit {result.returncode}, expected {expected}")
    return elapsed

def percentile(samples, fraction):
    ordered = sorted(samples)
    return ordered[min(len(ordered) - 1, int(round(fraction * (len(ordered) - 1))))]

def bootstrap_median_difference(a, b, rounds=2000):
    rng = random.Random(7)
    differences = sorted(statistics.median(rng.choices(b, k=len(b))) - statistics.median(rng.choices(a, k=len(a))) for _ in range(rounds))
    return differences[int(0.025 * rounds)], differences[int(0.975 * rounds)]

rng = random.Random(11)
print(f"{runs} runs per engine and case, interleaved, after {warmup} warm-up calls; times in ms")
for name, root, payload, expected in cases:
    samples = [[] for _ in engines]
    for _ in range(warmup):
        for engine in engines:
            call(engine, root, payload, expected)
    for _ in range(runs):
        order = list(range(len(engines)))
        rng.shuffle(order)
        for index in order:
            samples[index].append(call(engines[index], root, payload, expected))
    columns = "  ".join(f"p50 {percentile(samples_of, 0.5):6.2f}  p95 {percentile(samples_of, 0.95):6.2f}" for samples_of in samples)
    line = f"{name:<55} {columns}"
    if len(engines) == 2:
        a, b = samples
        low, high = bootstrap_median_difference(a, b)
        p50 = statistics.median(b) - statistics.median(a)
        p95 = percentile(b, 0.95) - percentile(a, 0.95)
        line += f"  Δp50 {p50:+.2f} [{low:+.2f}, {high:+.2f}]  Δp95 {p95:+.2f}"
    print(line, flush=True)
if len(engines) == 2:
    print(f"Δ = second engine minus first: {engines[1]} − {engines[0]}")
PY
