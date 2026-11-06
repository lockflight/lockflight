#!/usr/bin/env bash
# Hook latency: the PreToolUse decision runs before every tool call, so its cost is measured, not assumed.
#   tests/benchmark/hook-latency.sh <engine> [<other engine>]
# With one engine, prints p50 and p95 per case. With two (e.g. the build of main and the build of a branch), runs them
# interleaved, call by call, on the same fixture and prints both, the p50 difference with a 95% bootstrap confidence interval
# and the p95 difference, so machine noise lands on both sides alike. RUNS (default 1000) calls per engine and case, after a
# warm-up of WARMUP (default 50). Needs python3 (or python). Exits non-zero only when a call's exit code is not the case's
# expected one. Runs on macOS, Linux and Windows (Git Bash).
#
# Warm cases call the engine directly, as the engine's own cost, and through the plugin's hook shim the way Claude Code runs a
# hook command (a shell running the command line: /bin/sh on macOS and Linux, Git Bash on Windows), with the engine linked and
# its hash check remembered. Cold cases take COLD (default 20) samples, each the first call of a freshly copied engine: the first
# hook after an install, an update or a link, with the operating system's first-run checks of a new executable (Windows Defender,
# macOS code signing) and, through the shim, the hash check of the newly linked engine.
# JSON_OUT=<file> also writes every case's numbers there as JSON.
set -euo pipefail

if [ "$#" -lt 1 ] || [ "$#" -gt 2 ]; then
  echo "usage: $0 <engine> [<other engine>]" >&2
  exit 4
fi

here="$(cd "$(dirname "$0")" && pwd)"
py="$(command -v python3 || command -v python)" || { echo "python3 is needed" >&2; exit 4; }
# On Git Bash, Windows paths: Python and the engine are Windows programs, and Claude Code hands the engine Windows paths.
native() { if command -v cygpath > /dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi; }

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

# Engines by absolute path: every call runs in a fixture project.
engines=()
for engine in "$@"; do
  case "$engine" in /*|[A-Za-z]:*) ;; *) engine="$PWD/$engine" ;; esac
  [ -f "$engine" ] || { echo "no engine at $engine" >&2; exit 4; }
  engines+=("$(native "$engine")")
done

# The worktree case runs on its own copy, so the other cases measure a project without worktrees. Each engine has its own plain
# project, where the shim cases find it linked.
plains=()
for index in $(seq 1 "$#"); do
  plain="$fixture/plain$index"
  mkdir -p "$plain/src"
  printf 'version: 1\n' > "$plain/lockflight.yaml"
  printf 'class A {}\n' > "$plain/src/A.cs"
  plains+=("$(native "$plain")")
done

RUNS="${RUNS:-1000}" WARMUP="${WARMUP:-50}" COLD="${COLD:-20}" JSON_OUT="${JSON_OUT:-}" \
  "$py" - "$(native "$here/../../plugins/lockflight/bin/lockflight-hook")" "$(native "$fixture")" "$(native "$project")" \
  "${plains[@]}" "${engines[@]}" <<'PY'
import hashlib, json, os, random, shutil, statistics, subprocess, sys, time

shim, scratch, worktrees = sys.argv[1], sys.argv[2], sys.argv[3]
rest = sys.argv[4:]
plains, engines = rest[:len(rest) // 2], rest[len(rest) // 2:]
runs, warmup, cold_runs = int(os.environ["RUNS"]), int(os.environ["WARMUP"]), int(os.environ["COLD"])
windows = os.name == "nt"
true = shutil.which("true") or "/usr/bin/true"
# Claude Code runs a hook's command line in a shell: Git Bash on Windows, /bin/sh elsewhere.
shell = shutil.which("bash") if windows else "/bin/sh"
if shell is None:
    sys.exit("Git Bash's bash is not on PATH")

def edit(root, path):
    return json.dumps({"hook_event_name": "PreToolUse", "cwd": root, "tool_name": "Edit",
                       "tool_input": {"file_path": os.path.join(root, path), "old_string": "a", "new_string": "b"}})

def bash(root, command):
    return json.dumps({"hook_event_name": "PreToolUse", "cwd": root, "tool_name": "Bash", "tool_input": {"command": command}})

def link(engine, root):
    """As `lockflight link` leaves a project: the engine copied into .lockflight/bin with its SHA-256 recorded beside it."""
    folder = os.path.join(root, ".lockflight", "bin")
    shutil.rmtree(folder, ignore_errors=True)
    os.makedirs(folder)
    target = os.path.join(folder, "lockflight.exe" if engine.lower().endswith(".exe") else "lockflight")
    shutil.copy2(engine, target)
    with open(target, "rb") as binary:
        digest = hashlib.sha256(binary.read()).hexdigest()
    with open(os.path.join(folder, "lockflight.sha256"), "w", newline="\n") as record:
        record.write(digest + "\n")

def fresh_copy(engine, index):
    """A new file with the engine's bytes, as an install or update leaves it."""
    folder = os.path.join(scratch, "cold", str(index))
    shutil.rmtree(folder, ignore_errors=True)
    os.makedirs(folder)
    target = os.path.join(folder, os.path.basename(engine))
    shutil.copy2(engine, target)
    return target

def shim_line():
    return '"%s" pre-tool-use' % shim.replace("\\", "/")

# (name, kind, project of engine i, payload, expected exit code, cold too). Kinds: floor (an empty process), shell (an empty shell),
# engine (the engine called directly), shim (the plugin's hook command through the shell).
cases = [
    ("process floor (true)", "floor", lambda i: plains[i], None, 0, False),
    ("shell floor (empty hook command)", "shell", lambda i: plains[i], None, 0, False),
    ("allow: Edit of a source file", "engine", lambda i: plains[i], lambda root: edit(root, os.path.join("src", "A.cs")), 0, True),
    ("block: Edit of lockflight.yaml (ledger write)", "engine", lambda i: plains[i], lambda root: edit(root, "lockflight.yaml"), 2, True),
    ("block: Bash redirect and pipe into protected files", "engine", lambda i: plains[i], lambda root: bash(root, "echo x > global.json && cat src/A.cs | sort > out.txt"), 2, False),
    ("allow: Edit of a source file, three agent worktrees", "engine", lambda i: worktrees, lambda root: edit(root, os.path.join("src", "A.cs")), 0, False),
    ("allow through the hook shim: Edit of a source file", "shim", lambda i: plains[i], lambda root: edit(root, os.path.join("src", "A.cs")), 0, True),
    ("block through the hook shim: Edit of lockflight.yaml", "shim", lambda i: plains[i], lambda root: edit(root, "lockflight.yaml"), 2, True),
]

def call(kind, engine, root, payload, expected):
    if kind == "floor":
        argv = [true]
    elif kind == "shell":
        argv = [shell, "-c", ":"]
    elif kind == "engine":
        argv = [engine, "hook", "pre-tool-use"]
    else:
        argv = [shell, "-c", shim_line()]
    env = dict(os.environ, CLAUDE_PROJECT_DIR=root)
    started = time.perf_counter_ns()
    result = subprocess.run(argv, input=(payload or "").encode(), stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, env=env, cwd=root)
    elapsed = (time.perf_counter_ns() - started) / 1e6
    if result.returncode != expected:
        sys.exit(f"{' '.join(argv)}: exit {result.returncode}, expected {expected}: {result.stderr.decode(errors='replace')[:400]}")
    return elapsed

def cold_call(kind, engine, index, root, payload, expected, sample):
    if kind == "engine":
        return call(kind, fresh_copy(engine, index), root, payload, expected)
    # A new link: a new engine file and record, and no remembered hash check.
    link(engine, root)
    for leftover in ("lockflight.checked", "lockflight.checked.engine"):
        try:
            os.remove(os.path.join(root, ".lockflight", "bin", leftover))
        except FileNotFoundError:
            pass
    return call(kind, engine, root, payload, expected)

def percentile(samples, fraction):
    ordered = sorted(samples)
    return ordered[min(len(ordered) - 1, int(round(fraction * (len(ordered) - 1))))]

def bootstrap_median_difference(a, b, rounds=2000):
    rng = random.Random(7)
    differences = sorted(statistics.median(rng.choices(b, k=len(b))) - statistics.median(rng.choices(a, k=len(a))) for _ in range(rounds))
    return differences[int(0.025 * rounds)], differences[int(0.975 * rounds)]

for index, engine in enumerate(engines):
    link(engine, plains[index])

rng = random.Random(11)
results = []
print(f"warm: {runs} runs per engine and case, interleaved, after {warmup} warm-up calls; cold: {cold_runs} first calls of a "
      f"fresh engine copy; times in ms")
measured = [(c, "warm") for c in cases] + [(c, "cold") for c in cases if c[5] and cold_runs > 0]
for (name, kind, project_of, payload_of, expected, _), mode in measured:
    samples = [[] for _ in engines]
    roots = [project_of(i) for i in range(len(engines))]
    payloads = [payload_of(root) if payload_of else None for root in roots]
    if mode == "warm":
        for _ in range(warmup):
            for i, engine in enumerate(engines):
                call(kind, engine, roots[i], payloads[i], expected)
    for sample in range(runs if mode == "warm" else cold_runs):
        order = list(range(len(engines)))
        rng.shuffle(order)
        for i in order:
            if mode == "warm":
                samples[i].append(call(kind, engines[i], roots[i], payloads[i], expected))
            else:
                samples[i].append(cold_call(kind, engines[i], i, roots[i], payloads[i], expected, sample))
    if mode == "cold" and kind == "shim":
        for i, engine in enumerate(engines):
            link(engine, plains[i])
    columns = "  ".join(f"p50 {percentile(s, 0.5):7.2f}  p95 {percentile(s, 0.95):7.2f}" for s in samples)
    line = f"{mode:<4} {name:<55} {columns}"
    for i, s in enumerate(samples):
        results.append({"case": name, "kind": kind, "mode": mode, "engine": engines[i], "n": len(s),
                        "p50_ms": round(percentile(s, 0.5), 2), "p95_ms": round(percentile(s, 0.95), 2)})
    if len(engines) == 2:
        a, b = samples
        low, high = bootstrap_median_difference(a, b)
        p50 = statistics.median(b) - statistics.median(a)
        p95 = percentile(b, 0.95) - percentile(a, 0.95)
        line += f"  Δp50 {p50:+.2f} [{low:+.2f}, {high:+.2f}]  Δp95 {p95:+.2f}"
    print(line, flush=True)
if len(engines) == 2:
    print(f"Δ = second engine minus first: {engines[1]} − {engines[0]}")
if os.environ["JSON_OUT"]:
    with open(os.environ["JSON_OUT"], "w", encoding="utf-8") as out:
        json.dump({"runs": runs, "warmup": warmup, "cold": cold_runs, "shell": shell, "cases": results}, out, indent=2)
PY
