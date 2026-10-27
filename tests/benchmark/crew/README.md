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
3. Reads the evidence after the session: `redgreen status`, the ledger, the red/green record, `git status` and `git diff`
   of `src/`, and the session and subagent transcripts.

Every arm uses the same session model and the same per-run budget. What the agent says is not counted; the ledger is.

## Measures

| Column | Source |
|---|---|
| red accepted (red N) | `redgreen status` shows the cycle `RED (test projects locked)`; N is the position of the first `redgreen:red:accepted` among the ledger's red events |
| compile reds, other rejections | ledger `redgreen:red:COMPILE_RED` and `redgreen:red:rejected` events |
| rejection kinds | the kinds named in each `redgreen:red:rejected` event (`RED_IN_TEST`, `GREEN_BEFORE`, …), and the compile reds |
| accepted RED_OTHER | tests of the accepted cycle classed `RED_OTHER`; red/green now rejects that class, so only runs of older engines show any |
| FRs traced, untagged tests | the spec's requirements named by a `Requirement` trait of an accepted test, of all the spec's requirements; accepted tests with no such trait |
| test-author rounds | Agent tool calls naming the test author in the main session (0 in the plain arm) |
| status after test author | whether the main session's next tool call after each round was `redgreen status` (`first`), came later (`late`) or never followed the last round (`no`); `-` without a round |
| production files changed | `git status` of `src/` after the run: the stubs were committed, so anything here is a production change (the diff is kept as `production.diff`) |
| production writes | Write and Edit calls aimed at `src/` by any agent, refused or not |
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
over the sample (it may add test helpers too). `run.sh --setup-only <story> <dir>` seeds one without a session, to check
that a new story builds and that `redgreen start` passes.

## Stories

Every story is deterministic: fixed dates and amounts, no clock, network or randomness in what the tests need.

| story | what it exercises | the right handling |
|---|---|---|
| `shipping`, `coupons`, `bulk-pricing` | plain stubs with simple signatures | a red of RED_STUB or RED_ASSERT tests on the first try |
| `order-summary` | records, an async method with a `CancellationToken`, an exception as an outcome | the same |
| `discount-rules` | an existing method changed, no stubs: two scenarios already pass | say that those scenarios pass today (GREEN_BEFORE) instead of forcing a red for them |
| `loyalty-points` | **compile-red trap.** The API lives in a new namespace and differs from the spec's wording: a `readonly record struct` id, a record with `required` members, a `ValueTask` method with an extra `pointsBefore` argument, a `TimeProvider` with no time-faking package | read the declared signatures before writing tests; fake the clock with a `TimeProvider` subclass in the test project, not a new package |
| `gift-cards` | **test-side exceptions.** `Money` is complete and rejects negative amounts itself, so a test of the "-5.00" scenario throws in its own setup or passes before implementation; the seeded test helper `GiftCardBuilder.ExpiringOn` throws `NotImplementedException` from test code (RED_IN_TEST, not a stub red) | build cards without the broken helper or fix it (it is test code), test the zero amount, and report that negative amounts are already refused by `Money` |
| `receipt-lines` | **missing stub.** The plan declares `FormatLine(string, decimal, int width)`, but the committed stub lacks that overload, so tests for the width scenarios cannot compile | the test author stops and reports the missing member; the main session may declare it as a stub (`throw new NotImplementedException();`) and start another round; nobody implements it |
| `checkout-totals` | **trace across classes.** Eight requirements over four classes (tax, shipping, promo codes, the calculator composing them) and nine scenarios | every test tagged with the requirement it checks, so that all eight are traced |

[RESULTS.md](RESULTS.md) records the runs made so far.
