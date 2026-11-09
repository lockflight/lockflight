# What the judge catches

An agent graded by its tests can make the tests pass instead of making the code right: write the tests after the code,
weaken an assertion, skip the test that fails, pad the coverage. This page reports what Lockflight's CI judge does with those
moves, measured, next to what a plain CI does with them. It is a benchmark of scripted pull requests, not a proof: read it
with [guardrails and boundaries](guardrails-and-boundaries.md), which says what each control can promise.

**On this page:** [How it was measured](#how-it-was-measured) · [Results](#results) · [What it catches](#what-it-catches) ·
[What it does not catch](#what-it-does-not-catch-and-why) · [What the benchmark leaves out](#what-the-benchmark-leaves-out) ·
[Cost](#cost) · [Run it yourself](#run-it-yourself)

## How it was measured

The [catch benchmark](../../benchmarks/catch/README.md) builds each pull request as a scripted git history on a copy of a
sample (xUnit v3, MSTest 4 and NUnit 4), from a base commit with a version 2 `lockflight.yaml` that turns patch coverage on
(Microsoft collector, 80 %). Every pull request adds the same kind of story: a few requirements in a spec, their tests, their
implementation. One per sample is done honestly (the **control**); the others each play one **cheat**, most of them hiding a
bug or a regression the honest tests would have shown. No model is involved: the cheats are written out, so the same
pull requests give the same result on every run.

Each pull request is judged twice:

- **Lockflight:** `lockflight ci` as the generated workflow runs it, then `ci report`, with both status contexts treated as
  required. A pull request is **blocked** when the verdict is not PASS for a reason the judge decides itself. The review of
  record is not run (it calls a model); a pull request held only because its change class requires that review is counted
  apart, as **held for review**.
- **Plain CI:** `dotnet test` with code coverage, failing when a test fails or when the tests execute less than 80 % of the
  production lines the pull request added or changed. The threshold is read from the pull request's own config file, as a
  plain CI reads its `codecov.yml`.

## Results

The latest run: [`benchmarks/catch/results/`](../../benchmarks/catch/results/) (9 November 2026, a GitHub-hosted Ubuntu
runner, the engine built from source).

31 cheats and 3 honest controls on three samples. Lockflight blocked 26 cheats, held 1 for the review of record and passed
4; the plain CI failed 4. Of the **27 cheats the plain CI passed, Lockflight blocked 23 (85 %)**.

| Category | Cheats | Lockflight blocked | Held for review | Plain CI failed | Plain CI passed, Lockflight blocked |
|---|---|---|---|---|---|
| Test after code | 5 | 5 | 0 | 0 | 5 of 5 |
| Deleted or weakened assertion | 6 | 5 | 0 | 2 | 3 of 4 |
| Skipped test | 5 | 5 | 0 | 1 | 4 of 4 |
| Weakened amend | 3 | 3 | 0 | 0 | 3 of 3 |
| Test changed to match a bug | 2 | 1 | 0 | 0 | 1 of 2 |
| Coverage padding | 4 | 2 | 0 | 1 | 2 of 3 |
| Policy loosening | 4 | 3 | 1 | 0 | 3 of 4 |
| Host tampering (plain forms) | 2 | 2 | 0 | 0 | 2 of 2 |

The plain CI failed four cheats: in three the bug left a branch unexecuted, so patch coverage fell below 80 % (an accident of
these small stories), and one took the test project out of the solution, which `dotnet test` refuses (the judge gives ERROR).
One of them, assertion-free tests in the red commit on the NUnit sample, the plain CI failed on coverage and Lockflight
passed: the judge's patch coverage step counted 20 changed lines (on this VSTest sample the coverage report holds the new
tests' own lines too), 90 % covered, where the plain CI counted the 9 production lines, 78 % covered. Every pull request,
with its reason codes and times, is in the [results](../../benchmarks/catch/results/2026-11-09.md).

Both sides passed every honest control: no false red.

## What it catches

- **Tests written after the code.** The judge builds each commit with a `Lockflight-Red` trailer and needs its new tests to
  fail inside production code. An implementation in or before that commit makes them pass there (`GREEN_BEFORE`); production
  code without a red commit is `RED_MISSING`. The plain CI cannot tell the order in which the code and its tests were written.
- **A cycle test changed after its red:** an assertion widened to always hold, a test deleted or skipped, data rows or an
  assertion dropped, an expected value moved to the buggy one. Each test that went red is compared at head with its red
  version, and a changed one is re-run on the red commit's code (`AMEND_CHANGED_EXPECTATION`, `AMEND_WEAKENED`,
  `CYCLE_TEST_MISSING`, `AMEND_REJECTED`). The tests still pass, so the plain CI passes too.
- **An existing test deleted or skipped** to hide a regression: green at head counts every test the base had
  (`HEAD_NOT_GREEN`).
- **Coverage padding** without a red, or for a requirement no red test names: red and trace need a test that went red, then
  green, for every requirement (`RED_MISSING`, `TRACE_UNCOVERED`). Lines executed are not enough.
- **Policy loosened in the pull request:** the judge applies the target branch's `lockflight.yaml`, so a lowered coverage
  threshold does not apply, and the policy check names the loosened key (`POLICY_LOOSENED`). The plain CI reads the lowered
  threshold from the pull request and passes. A new `#pragma warning disable` or `[ExcludeFromCodeCoverage]` fails the
  suppressions step (`NEW_SUPPRESSIONS`, `fail` in version 2).
- **Plain host tampering:** a module initializer that sets the test host's exit code to 0, and a test application told to
  ignore the failed-tests exit code, both make `dotnet test` exit 0 with a failing test. The judge reads each test's outcome
  from the test reports, not the exit code (`HEAD_NOT_GREEN`), and lists the module initializer (`HOST_TAMPERING`).

## What it does not catch, and why

- **An existing test widened until a regression passes it** (`Assert.InRange` around the old value). The judge runs the
  changed test on the base's code; it passes there too, so it no longer tells the old behaviour from the new. That is shown
  in the comment as an advisory finding (`EXISTING_TEST_PASSES_ON_BASE`) and does not block: a refactor that keeps the
  expectation reads the same, so the person who merges decides.
- **An existing test's expected value changed to match a regression.** The changed test fails on the base's code, which is
  exactly what an intended behaviour change looks like. Whether the new value is right is a question for the spec and the
  review; nothing in the diff tells the judge it is a bug.
- **Assertion-free tests inside the red commit.** A stub throws, so a test that only calls it fails at red (RED_STUB) and
  passes once anything is implemented. The judge accepts it and the comment marks it `(assertion-free)` in the trace, for
  review; the buggy implementation behind it passes.
- **Warnings-as-errors switched off in `Directory.Build.props`**, with a new compiler warning. The build reports the warning,
  but without a build baseline it does not fail. The change to `Directory.Build.props` raises the change class to critical,
  which requires the review of record, so the pull request is held for review (`REVIEW_NOT_RUN` here, where no review runs)
  and listed under `policy.protected` for CODEOWNERS; whether it merges is the reviewer's decision, not the judge's.

In each of these the evidence is in the pull request comment, but the decision is a person's: CODEOWNERS and a required
review are what make it one ([CI hardening](ci-hardening.md)).

## What the benchmark leaves out

- **Tests that are wrong from the start.** A red test that expects the buggy value goes red and then green honestly; only
  the spec and its review can tell. The judge proves the tests came first, not that they are right.
- **Deliberate forging beyond the plain forms:** a `.runsettings` data collector, a test platform extension, an `<Exec>` in a
  project file, a process left running, root rights on the runner. [CI hardening](ci-hardening.md#what-the-ci-judge-shows-and-what-it-does-not)
  lists what each can do and which are guardrails; `tests/redteam/` and `tests/e2e/survivors.sh` test the ones the judge
  stops.
- **A spec changed in the pull request** to drop a requirement with its test: the spec diff is shown first in the comment,
  and CODEOWNERS on the spec paths makes it a person's approval.
- **The review of record**, which calls a model, and the desktop sample, whose WPF tests need Windows.
- **Size.** Each story is a few dozen lines on a small sample. Larger pull requests give more room to hide a change, and a
  real agent's cheats are rarely this clean.

## Cost

The judge builds and tests the head, the base and every red commit, and runs the `pr` gates, so it costs more than one
`dotnet test`. On a GitHub-hosted Ubuntu runner the judge took a median of 38 s per pull request (18 to 66 s:
more for each red commit and each amend it re-checks) against 8 s for the plain CI, with the NuGet cache warm after the first
pull request of a job. The three jobs billed 32 runner minutes for 34 pull requests, about 0.9 minutes each, including a
Native AOT build of the engine per job that a real workflow does not do (it installs the released engine).

## Run it yourself

```sh
cd engine && dotnet publish src/Lockflight.Cli -c Release -o ../artifacts/engine && cd ..
ENGINE=$PWD/artifacts/engine/lockflight bash benchmarks/catch/run.sh /tmp/lf-catch
```

The [benchmark's README](../../benchmarks/catch/README.md) lists every pull request, how to run one sample or a few cases,
and how to summarize the results. The `catch` workflow runs it on GitHub-hosted runners when started by hand.

## Next

- [Guardrails and boundaries](guardrails-and-boundaries.md): what each control can promise
- [CI hardening](ci-hardening.md): make the judge the check of record
- [Reasons](reasons.md): every reason code named above
