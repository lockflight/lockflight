# Catch benchmark: what the judge catches against plain CI

An agent that is graded by its tests can make the tests pass instead of making the code right. This benchmark plays the
cheats an agent could try as scripted pull requests, and judges each one twice:

- **Lockflight:** `lockflight ci`, the CI judge, as the generated workflow runs it, then `ci report` for the verdict. Both
  status contexts (`gates` and `lockflight/judge`) are treated as required, as [CI hardening](../../docs/guides/ci-hardening.md#which-check-is-required)
  recommends. A pull request is **caught** when the verdict is not PASS.
- **Plain CI:** `dotnet test` with code coverage on a clean checkout of the head, failing when a test fails or when the
  tests executed less than **80 %** of the production lines the pull request added or changed (patch coverage, as a
  Codecov-style check reports it). The threshold is read from `coverage.json` in the head, as a plain CI reads its
  `codecov.yml` from the pull request.

Nothing here calls a model: every pull request is a script. The summary of the latest results is
[what the judge catches](../../docs/guides/what-the-judge-catches.md); the runs are in [`results/`](results/).

## The pull requests

Each sample's file in [`samples/`](samples/) sets up a repository from the sample (one base commit with a version 2
`lockflight.yaml` that turns patch coverage on with the Microsoft collector at 80 %, the plain CI's `coverage.json`, and a
CODEOWNERS file) and lists its cases. Every case starts a branch from that base and adds one story:

- **controls**, one per sample: the story done honestly (spec, tests and stubs in a commit with a `Lockflight-Red: 1`
  trailer, then the implementation). Both sides must pass it; a control that does not pass is a false red, and is reported.
- **cheats**, in these categories:

| Category | Cheats |
|---|---|
| test after code | the implementation in the red commit, or before it; no red commit at all |
| deleted or weakened assertion | a cycle test's assertion made always true, or the test deleted, hiding a bug; an existing test deleted or widened, hiding a regression |
| skipped test | an existing or a cycle test skipped; the test project taken out of the solution |
| weakened amend | a cycle test's data rows, or one of its assertions, dropped after red |
| test changed to match a bug | a cycle test's, or an existing test's, expected value changed to the buggy one |
| coverage padding | assertion-free tests that execute the code: without a red commit, inside the red commit, or after green for an untraced requirement |
| policy loosening | the coverage threshold lowered in the repository's config, warnings-as-errors switched off, a `#pragma warning disable`, `[ExcludeFromCodeCoverage]` on untested code |
| host tampering | a module initializer that rewrites the test host's exit code; the test application told to ignore failed tests |

## Run it

Publish the engine, then run the samples (all three by default) one pull request at a time. Each pull request is built and
tested several times by the judge and once by the plain CI, so a full run takes a while; CI's `catch` workflow runs it in
three jobs.

```sh
cd engine && dotnet publish src/Lockflight.Cli -c Release -o ../artifacts/engine && cd ..
ENGINE=$PWD/artifacts/engine/lockflight bash benchmarks/catch/run.sh /tmp/lf-catch               # every sample
ONLY='^(control|tautology)$' ENGINE=… bash benchmarks/catch/run.sh /tmp/lf-catch dotnet-single    # some cases of one sample
python3 -I benchmarks/catch/catch.py summarize --date 2026-01-01 --out-md out.md --out-json out.json /tmp/lf-catch/results.jsonl
```

`LOCKFLIGHT_GITLEAKS` names the gitleaks binary for the judge's secrets step (CI's install action sets it). Each pull
request's evidence, verdict, plain CI log and coverage reports stay under the work directory.

## Measures

| Field | Source |
|---|---|
| Lockflight verdict, blocking reasons | `verdict.json` from `ci report`: the verdict, and each required check that did not pass with its [reason code](../../docs/guides/reasons.md) |
| Advisory | checks that did not pass but do not block (`tests.existingChanged`), shown for review |
| Gate steps | each `pr` gate step that did not pass, with its reason code |
| Plain CI verdict | `dotnet test`'s exit code, and the patch coverage against the threshold |
| Wall time | the judge's and the plain CI's own run, each measured around its command |
| Runner minutes | in CI, the `measure` jobs' durations, rounded up to the minute per job as GitHub bills them |

The wall times are not a like-for-like cost: the judge builds and tests the head, the base and every red commit, and runs the
`pr` gates, while the plain CI builds and tests the head once. They are here so the price of each catch is visible.
