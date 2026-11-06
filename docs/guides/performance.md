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

RESULTS

## What they mean for daily use

MEANING

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
