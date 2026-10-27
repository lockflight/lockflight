# Crew benchmark: test author against a plain agent

An optional crew agent is worth shipping only if it does better than the agent a user already has. This benchmark measures
the red step, the one job of the `lockflight-crew:test-author` agent, on seeded stories with a control.

## What a run does

For each story in [`stories/`](stories/), each run and each arm:

1. Copies [`samples/dotnet-single`](../../../samples/dotnet-single/) into a temporary directory, adds the story's `spec.md`,
   `plan.md`, `tasks.md` and its declared stubs (`stub/`, members whose body is `throw new NotImplementedException();`),
   commits, then plays the person: `approve spec`, `approve tasks` and `redgreen start`, with `CLAUDECODE` unset, in that
   temporary copy only.
2. Starts a headless session (`claude -p`, `--permission-mode acceptEdits`, Bash allowed only for the engine, `dotnet` and
   read-only commands) asking for the red step of `US1`:
   - **plain:** only the core `lockflight` plugin; the main session writes the tests.
   - **crew:** the core and the `lockflight-crew` plugins, with the same prompt plus one sentence saying to use the test
     author, as the feature skill describes. The test author inherits the session's model.
   - **`crew-<model>`** (for example `crew-haiku`, `crew-opus`): the same, with the test author on another model. The
     shipped agent has no `model` line; the script writes a copy of the crew plugin into the output directory whose
     agent file adds `model: <model>` to the frontmatter, and loads that copy instead.
3. Reads the evidence after the session: `redgreen status`, the ledger, `git status` of `src/`, and the session and
   subagent transcripts.

Every arm uses the same session model and the same per-run budget. What the agent says is not counted; the ledger is.

## Measures

| Column | Source |
|---|---|
| red accepted (red N) | `redgreen status` shows the cycle `RED (test projects locked)`; N is the position of the first `redgreen:red:accepted` among the ledger's red events |
| compile reds, other rejections | ledger `redgreen:red:COMPILE_RED` and `redgreen:red:rejected` events |
| rejection kinds | the kinds named in each `redgreen:red:rejected` event, and the compile reds |
| test-author rounds | Agent tool calls naming the test author in the main session (0 in the plain arm) |
| production files changed | `git status` of `src/` after the run: the stubs were committed, so anything here is a production change |
| guard blocks | ledger `hook.blocked` events |
| wall s | the session's wall time |
| tokens (all), test-author tokens | `message.usage` of every API message in the session and subagent transcripts (input, output, cache creation and cache reads, each message once); the test author's share is the messages with its `attributionAgent` |
| test-author model | `message.model` of the test author's messages |
| cost USD | the session's `total_cost_usd` from `--output-format json`; there is no per-agent price. The per-arm table adds the cost per accepted red |

## Running it

Needs `claude` (logged in), the .NET SDK the sample pins, `jq`, `git` and `perl`. It spends real model usage.

```sh
dotnet publish engine/src/Lockflight.Cli -c Release -r osx-arm64 -o out
ENGINE="$PWD/out/lockflight" tests/benchmark/crew/run.sh                     # every story × 2 runs × plain,crew, sonnet
ENGINE="$PWD/out/lockflight" tests/benchmark/crew/run.sh --runs 3 --arms plain,crew,crew-opus,crew-haiku
ENGINE="$PWD/out/lockflight" tests/benchmark/crew/run.sh --stories shipping,coupons --runs 3 --model haiku \
  --max-budget-usd 1.50 --total-budget-usd 10 --out /tmp/crew-bench
tests/benchmark/crew/run.sh --report-only /tmp/crew-bench                   # rebuild the table from an earlier run
```

The model defaults to `sonnet`: a headless run cannot inherit an interactive session's model. The total budget stops the
script before a run that could pass it. Results (per-run table and per-arm summary) are printed and written to
`<out>/report.md`; each run's directory keeps its project, ledger, transcripts and session output for inspection.

A story is a directory with `spec.md`, `plan.md`, `tasks.md` (story `US1`, its stub task done) and `stub/`, a tree copied
over the sample. `discount-rules` has no stubs: it changes an existing method, so part of its behaviour already passes.
Add stories that exercise what the brief is about: awkward signatures, records, exceptions as outcomes, nullable warnings
(the sample treats warnings as errors).

[RESULTS.md](RESULTS.md) records the runs made so far.
