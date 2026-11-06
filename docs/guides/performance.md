# Performance

What Lockflight costs in time: the guard on every tool call, the Stop gate at the end of a turn, and the CI judge on a pull
request. The numbers below are measured, on GitHub-hosted runners, by the weekly [`perf` workflow](../../.github/workflows/perf.yml).
For anyone deciding whether the hooks are cheap enough, and for contributors changing the hook path or the gates.

> [!NOTE]
> These numbers are recorded, not enforced. No check fails when a change makes the hook or a gate slower; a regression gate
> may come later. Until then, compare two engines with [`hook-latency.sh`](#measure-on-your-machine) before and after a change
> on the hook path.

**On this page:** [Measured numbers](#measured-numbers) · [What they mean for daily use](#what-they-mean-for-daily-use) ·
[Measure on your machine](#measure-on-your-machine) · [How the workflow measures](#how-the-workflow-measures)

## Measured numbers

Measured on 2026-11-06 by [run 42289310713](https://github.com/lockflight/lockflight/actions/runs/42289310713) of the
workflow, on GitHub-hosted runners. The engine was built from source (it reports `0.0.0-local`) at `c43b5699`, the pull
request that added this workflow merged onto `main` at `f1ccfd67`. On the Windows runner Defender's real-time scanning was
off, as the image has it; on a developer machine with it on, the cold rows on Windows are likely higher.

| Machine | Date | Runner image | Engine |
|---|---|---|---|
| macos-latest | 2026-11-06 | macos26 20261026.0455.2 | 0.0.0-local (c43b5699) |
| ubuntu-latest | 2026-11-06 | ubuntu24 20261101.360.1 | 0.0.0-local (c43b5699) |
| windows-latest | 2026-11-06 | win25-vs2026 20261103.298.1, Defender real-time scanning off | 0.0.0-local (c43b5699) |

**PreToolUse hook, p50 / p95 in ms**

| | macos-latest | ubuntu-latest | windows-latest |
|---|---|---|---|
| warm: process floor (true) | 2.8 / 6.0 | 0.5 / 0.6 | 10.5 / 11.9 |
| warm: shell floor (empty hook command) | 4.9 / 7.4 | 0.5 / 0.6 | 20.1 / 22.1 |
| warm: allow: Edit of a source file | 16.2 / 25.5 | 6.9 / 8.0 | 19.0 / 20.9 |
| warm: block: Edit of lockflight.yaml (ledger write) | 15.2 / 24.1 | 7.1 / 8.7 | 16.9 / 18.5 |
| warm: block: Bash redirect and pipe into protected files | 18.7 / 41.8 | 7.5 / 9.3 | 16.9 / 18.6 |
| warm: allow: Edit of a source file, three agent worktrees | 21.8 / 35.1 | 7.2 / 8.3 | 25.8 / 27.9 |
| warm: allow through the hook shim: Edit of a source file | 34.4 / 46.7 | 8.6 / 9.4 | 70.4 / 77.2 |
| warm: block through the hook shim: Edit of lockflight.yaml | 25.3 / 38.6 | 8.6 / 9.8 | 68.4 / 77.5 |
| cold: allow: Edit of a source file | 30.4 / 42.8 | 8.2 / 8.6 | 118.3 / 257.1 |
| cold: block: Edit of lockflight.yaml (ledger write) | 27.8 / 36.1 | 8.3 / 8.5 | 107.1 / 219.7 |
| cold: allow through the hook shim: Edit of a source file | 87.2 / 98.2 | 23.5 / 24.1 | 270.3 / 492.5 |
| cold: block through the hook shim: Edit of lockflight.yaml | 72.4 / 112.2 | 23.9 / 24.9 | 245.3 / 285.9 |

**Stop gate on a ten-project solution, median of 3 runs (whole run)**

| | macos-latest | ubuntu-latest | windows-latest |
|---|---|---|---|
| leaf change, incremental | 7.4 s | 4.9 s | 8.2 s |
| leaf change, --full | 9.1 s | 5.5 s | 8.9 s |
| core change, incremental | 6.2 s | 5.4 s | 8.8 s |
| core change, --full | 7.5 s | 5.4 s | 8.7 s |

**CI judge (`lockflight ci`), wall time per run, in the order the suite runs them**

| | macos-latest | ubuntu-latest | windows-latest |
|---|---|---|---|
| dotnet-single, 001-shipping (PASS) | 44.4 s | 26.1 s | 60.5 s |
| dotnet-single, 002-cheat (FAIL) | 31.7 s | 25.7 s | 50.4 s |
| dotnet-single, 001-shipping (FAIL) | 41.8 s | 31.2 s | 52.1 s |
| dotnet-single, 003-blind (FAIL) | 59.8 s | 35.6 s | 58.4 s |
| dotnet-mstest, 001-lending (PASS) | 36.3 s | 22.7 s | 44.3 s |

## What they mean for daily use

- **The guard costs less than the hook around it.** Warm, the engine decides a tool call in about 7 ms on Linux, 15 to 19 ms
  on macOS and 17 to 19 ms on Windows, allowed or blocked alike (a block also writes the ledger). The shim that Claude Code
  actually runs adds the shell: about 2 ms on Linux, 10 to 20 ms on macOS, and about 50 ms on Windows, where every process
  Git Bash starts costs 10 to 20 ms. Claude Code runs the hook before each tool call, so an agent turn of 30 tool calls spends
  about 0.3 s in it on Linux, 1 s on macOS and 2 s on Windows: small next to the model's own time, but on Windows the shell
  is most of it. The shim is written to start no extra process on its common path for that reason.
- **The first call after an install, an update or a link is slower**, because the shim hashes the newly linked engine before
  it trusts it, and the operating system may scan a new executable: about 25 ms on Linux, 70 to 90 ms on macOS and 250 to 270
  ms on Windows on these runners. It happens once per engine file. Windows Defender's real-time scanning was off on the Windows
  runner, so on a developer machine where it is on, or with another antivirus that scans every new executable, expect that
  first call to take longer.
- **Agent worktrees** (`.claude/worktrees/`) add a few milliseconds where they are many: with three of them, about 5 ms on macOS
  and 7 ms on Windows, and nothing measurable on Linux.
- **The Stop gate is a build and a test run, so its time is your solution's.** On the ten-project sample, an incremental run
  and `--full` are within two seconds of each other, often within one: when the tests are this quick, an up-to-date MSBuild
  build dominates either way, and the run-to-run noise is as large as the difference. The incremental choice saves the build and test time of
  the projects a change does not reach, which grows with slow tests and with how much of the solution stays untouched; read
  each step's `(N ms)` in your own `gate run` to see where your time goes. The Stop hook gives the gate at most 540 s
  ([configuration](configuration.md#the-stop-profile-builds-what-the-change-affects)).
- **The CI judge takes about half a minute to a minute** on the small samples: it builds and tests the pull request more than
  once (at its red commits and at its head) and runs the gates. Most of that is `dotnet build` and `dotnet test`, so on a real
  solution expect a few times your own CI build and test time. Linux runners are the fastest; macOS and Windows take up to
  twice as long. The judge on a desktop solution (WPF, judged on Windows) is not yet measured.

## Measure on your machine

Publish the engine (Native AOT, as users get it), then run each script against it. All three run on macOS, Linux and Windows
(Git Bash), and need python3 (`python` on Windows is fine).

```sh
cd engine && dotnet publish src/Lockflight.Cli -c Release -r osx-arm64 -o ../out && cd ..   # linux-x64, win-x64
engine="$PWD/out/lockflight"; [ -f "$engine.exe" ] && engine="$engine.exe"

# PreToolUse latency: warm (RUNS calls after WARMUP) and cold (COLD first calls of a fresh engine copy)
RUNS=500 COLD=20 bash tests/benchmark/hook-latency.sh "$engine"
# Two engines interleaved, e.g. main against a branch: adds the p50 difference with its 95% confidence interval
bash tests/benchmark/hook-latency.sh main/out/lockflight branch/out/lockflight

# The Stop gate incremental against --full on a generated ten-project solution (needs the .NET 10 SDK and network once)
ENGINE="$engine" bash tests/gates/incremental-timing.sh 3

# The CI judge's wall time on the samples: one line per `lockflight ci` run
JUDGE_TIMES=judge-times.txt ENGINE="$engine" bash tests/e2e/ci-judge.sh "$(mktemp -d)/ci-judge"
```

Each script also writes JSON when asked (`JSON_OUT=<file>` for the first two); `tests/benchmark/perf-summary.py` turns the
three outputs into the tables above. To time your own project instead of the fixtures, run the gate there and read its
`(N ms)` per step: `.lockflight/bin/lockflight gate run`, and again with `--full`.

## How the workflow measures

Each run builds the engine with Native AOT on a fresh runner (`ubuntu-latest`, `windows-latest`, `macos-latest`) and runs:

- **Hook latency** ([`hook-latency.sh`](../../tests/benchmark/hook-latency.sh)): 500 calls per case after 50 warm-up
  calls, and 20 cold calls. *Engine* rows call `lockflight hook pre-tool-use` directly with a Claude Code-shaped payload, the
  engine's own cost. *Hook shim* rows run the plugin's hook command the way Claude Code runs it: a shell (`/bin/sh` on macOS
  and Linux, Git Bash on Windows) running `lockflight-hook pre-tool-use`, with the engine linked into `.lockflight/bin/` and its
  hash check already remembered. A *cold* call is the first call of a freshly copied engine (after an install, an update or a
  link), so it includes the operating system's first-run checks of a new executable; through the shim it also includes the
  hash check of the newly linked engine. The *floor* rows are an empty process and an empty shell: what any hook costs before
  Lockflight does anything.
- **Stop gate** ([`incremental-timing.sh`](../../tests/gates/incremental-timing.sh)): a generated solution of ten projects
  (four of them xUnit v3 test projects on Microsoft.Testing.Platform). Each run starts from the state the last Stop left
  (everything built, a PASS recorded), changes one file and times `gate run --profile stop`, incremental and with `--full`:
  a leaf change (2 of 10 projects affected) and a change to a core project (7 of 10). The median of 3 runs.
- **CI judge** ([`ci-judge.sh`](../../tests/e2e/ci-judge.sh)): the wall time of each `lockflight ci` run in the suite, on copies
  of [`samples/dotnet-single`](../../samples/dotnet-single/) and [`samples/dotnet-mstest`](../../samples/dotnet-mstest/) with
  a scripted pull request history: an honest story (PASS), a red commit that holds the implementation and a test weakened
  after green (FAIL), a pull request that adds a secret (FAIL at the gates), the same story on MSTest (PASS).

Real-time scanning on the Windows runner is left as the image has it (the tables say whether it was on), unlike the `ci`
workflow, which turns it off for speed. Hosted runners are shared machines: expect a few milliseconds of noise on the hook and
seconds on builds between runs. Each run's job summary has its own table, and its `perf-<os>` artifact holds the JSON.

## Next

- [Troubleshooting: the Stop gate is slow](troubleshooting.md#the-stop-gate-is-slow): read what the gate chose, and why
- [Configuration: how the stop profile chooses projects](configuration.md#the-stop-profile-builds-what-the-change-affects)
- [Tests](../../tests/README.md): every cross-component suite, the benchmarks among them
