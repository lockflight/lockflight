# Configuration reference

What you can set in `lockflight.yaml`, which paths are always protected, how gate profiles judge a change, and the files and
variables around them. For the person who owns a project's policy.

**On this page:** [`lockflight.yaml`](#lockflightyaml) · [Built-in protected paths](#built-in-protected-paths) ·
[Gate profiles](#gate-profiles) · [The format step](#the-format-step-judges-the-change-not-the-file) ·
[Baselines](#baselines-brownfield-adoption-human-only) · [Lifecycle commands](#lifecycle-commands-standard-profile) ·
[Environment variables](#environment-variables)

## `lockflight.yaml`

Strict YAML: unknown keys and wrong types are errors with a position. In CI, pull requests are judged with the **base
branch's** copy.

> [!NOTE]
> The file is protected — the agent cannot edit it; change it yourself and commit.

```yaml
version: 1                      # optional; when present it must be 1

protected:                      # extra globs the agent must not change (the built-in list always applies)
  - "src/Payments/**"
  - "db/migrations/**"

stop:
  gate: false                   # run the stop profile when the agent ends a turn; also lets a task be marked
                                #   completed only after the stop gate passed for the current changes
  target: .                     # directory the stop gate builds and tests

gates:
  build: Shop.sln               # optional: the solution or project to build and test (default: the one in the target)
  timeoutSeconds: 600           # whole-run budget, 1–7200 s; child processes are killed when it runs out; it also
                                #   ends a redgreen red's FLAKY re-runs (counted from the red's start)
  test:
    framework: net10.0          # run tests for one target framework of multi-targeted projects
  coverage:
    collector: none             # none | microsoft | coverlet  (patch coverage in the pr profile); every test project
                                #   must reference the collector package (doctor checks): on Microsoft.Testing.Platform
                                #   Microsoft.Testing.Extensions.CodeCoverage or coverlet.MTP, on VSTest
                                #   Microsoft.NET.Test.Sdk (built in) or coverlet.collector
    patchThreshold: 80          # % of changed coverable lines that tests must execute
    minChangedLines: 10         # below this many coverable changed lines, coverage is not judged (N/A)

units:                          # workspace shell or monorepo; omit for a single repository
  - path: services/orders       # relative to this file, no '..'; units cannot be nested
    build: Orders.sln           # optional: the solution or project to build (default: the one in the directory)
    kind: auto                  # auto | dotnet | none  (none: guarded, not built — e.g. a TypeScript or gitops repository)
    test:
      framework: net8.0         # this unit's test target framework
      stopFilter: "Category!=Integration"   # tests the stop gate skips (they still run in the pr profile)
  - path: web/admin
    kind: none

review:                         # what the review of record runs with (both optional)
  model: sonnet                 # Claude Code model name (default sonnet; opus to review with Opus)
  maxUsd: 3.00                  # the reviewer's --max-budget-usd, above 0 and at most 100 (default 3.00); on a
                                #   subscription an estimate at API prices, not a bill
  target: origin/main           # the branch the change targets (default: the branch origin/HEAD points at, else main)
```

**Review of record.** A `lockflight review` run counts as the review of record only when it uses `review.model` and
`review.maxUsd` (or the defaults), its base commit is the merge base of HEAD and `review.target` (neither an older base nor a
recent commit on the branch, which would review only part of the change), and everything it reads is committed. The base defaults to the target. A run with `--model`, `--budget`, an outdated base or uncommitted or untracked files in the reviewed paths is still recorded, says why it is not of record, and
exits NOT-EARNED when it finds nothing.

**Choosing the review model.** The default is `sonnet`. On our review benchmark of seeded defects in a small sample
project, `sonnet` and `opus` found the same defects, with no false FAIL on clean changes for either, while `sonnet` cost about
half as much, ran faster and filed fewer minor findings; `opus` more often cited every location of a defect that spans several
files. That is detection on seeded cases, not yet measured on large real codebases. To review with Opus, set it in the manifest:

```yaml
review:
  model: opus
```

A manifest that sets `review.model` keeps that model when the default changes; a project without the setting follows the
default. Runs already recorded keep the model and the record status they were run with. `--model` on a single run is still
recorded but not of record (above).

**How the reviewer runs.** `lockflight review` starts `claude -p` with every mode set explicitly, so a change of Claude Code's
defaults does not change the reviewer: no user, project or local settings (so no hooks and no plugins), no MCP servers, only the
Read, Grep and Glob tools, and the `dontAsk` permission mode, which denies anything that would ask. When `ANTHROPIC_API_KEY` is set
it also runs in bare mode (`--bare`), which uses only that key; without it the reviewer uses your Claude Code sign-in
(subscription).

**Test platforms.** The test step runs whichever platform `dotnet test` uses in the target directory, decided as the SDK
decides it: by the nearest `global.json`'s `test.runner`.

- **Microsoft.Testing.Platform (xUnit v3):** results are read from the console summary.
- **VSTest (xUnit v2, NUnit, MSTest):** results are read from TRX reports written to `.lockflight/local/test-results/`.
  - A non-zero exit without a failed test is ERROR.
  - A run that did not complete (a crashed or aborted test host drops its results) is ERROR, and a test that timed out, was
    aborted or errored FAILs. Both are judged before the known-failing baseline, which covers only failures it can see.
- **On both:** a run with no executed test is NOT-EARNED, never PASS, whatever the runner's exit code. That includes a run in
  which every test was skipped, which Microsoft.Testing.Platform reports as "Zero tests ran" with exit code 0, a summary that
  does not say how many tests executed, and a `test.stopFilter` that matches no test (the detail names the filter). A filter that
  leaves one test project with nothing to run, while others execute tests, is judged by the tests that ran; the detail names the
  project that ran none.

Red/green and trace run xUnit v3 (natively or through Microsoft.Testing.Platform) and xUnit v2 on VSTest.

- **xUnit v3 runs:** natively, through the in-process runner the test assembly references; its version picks the report switch
  (`-xml` up to 3.x, `-result-xml` from 4.0), so both lines work.
- **xUnit v2 listing:** tests and their `Requirement` traits are read from the built assemblies' metadata, because VSTest's
  listing has neither.
- **xUnit v2 runs:** tests run with `dotnet test <dll> --filter`, and failures are read from the TRX. An assertion failure in a test
  that calls production code counts as RED_ASSERT, and a `NotImplementedException` thrown by production code as RED_STUB.
- **Other frameworks** (NUnit, MSTest) get the gates only.

**Units.** A workspace shell keeps `lockflight.yaml`, `.claude/`, Spec Kit and `specs/` at its root, and its service
repositories (each with its own `.git`) beneath it. Each repository is listed as a unit. A monorepo lists its buildable
directories the same way.

- **Protection:** the guard protects every unit's control files (next section).
- **Workspace-wide gates:** the gates see every unit repository. Changed lines (format, patch coverage), secret scanning,
  `policy.protected` and the Stop gate's cache all ask each child repository's git, and report paths relative to the shell.
- **Per-unit baselines:** `baseline tests|build|deps --path <unit>` writes `.lockflight/baselines/<unit>/…`. It builds and tests
  the unit with its own settings (`build:`, `test.framework`, `test.stopFilter`), as a workspace run does.
- **Base ref:** with `--base`, the same ref must exist in every unit repository.
- **Gate runs at the shell root** cover the units. `gate run` and the Stop gate run every check that reads the whole
  workspace once: format, and in `pr` also `policy.protected` and secrets. They build and test each changed .NET unit in its
  own directory, two at a time, on one shared budget.
  - The stop profile judges a unit whose state differs from its last PASS (`.lockflight/local/stop-pass-<unit>`). The state is
    the unit's HEAD (a monorepo unit: its directory's tree), its uncommitted and new files, and the shared build inputs outside
    every unit (C# and MSBuild files, solutions, `global.json`, NuGet and editor configuration), committed or not. A commit
    is judged like an edit, and a unit with no PASS yet is judged once. A unit back at a passed state is skipped.
  - `gate run` also judges every unit with uncommitted changes. The Stop gate skips one whose state matches its cached PASS.
    A stop-profile PASS is remembered by both. The edit profile runs format only.
  - `--unit a,b` limits a run to those units; `--path <unit>` (or a directory inside it) runs one unit with its own `build:`, test framework and
    stop filter, and so does the Stop gate when `stop.target` lies inside a unit; the root's `gates.build` applies only outside every unit.
  - The verdict is the worst of all steps.
- **The lifecycle across units:** specs, approvals and phase checks stay in the shell.
  - Red/green, trace and review build each .NET unit in its own directory and list and run the tests of all of them.
  - A feature narrows that to the units its `tasks.md` names with `[unit:services/orders]`. `phase check tasks` rejects a tag
    that names no unit.
  - The test lock covers the cycle's test projects in any unit, and known-failing tests are read from every unit's baseline.
- **Stop time:** the Stop gate records each unit's time in the local ledger. `doctor` names the units whose 95th percentile over
  14 days exceeds 2 minutes.
- **`test.stopFilter`** keeps slow tests out of the Stop gate and leaves them to `pr`. On VSTest it is passed to `--filter`
  as is. On Microsoft.Testing.Platform only one trait condition is supported (`Category!=Integration` becomes
  `--filter-not-trait Category=Integration`).
- **Start directory:** start Claude Code at the shell root. Claude Code reads project settings only from the directory a
  session starts in, so a session started inside a unit loads neither the shell's settings nor its hooks.
  - With the plugin installed at user scope, Lockflight's guard still runs there, and the session banner says that the
    shell's settings did not load.

The **stop** profile's budget is `min(gates.timeoutSeconds, 540)`: the plugin's Stop hook is killed after 600 s
(`plugins/lockflight/hooks/hooks.json`), and the gate must end — and report — before that. A step that runs out of the capped
budget is ERROR and says so; a build and test run that needs longer belongs in CI (`gate run --profile pr`). Only the stop
profile is capped; `baseline tests` and the `pr` profile use the full `gates.timeoutSeconds`.

### Built-in protected paths

These always apply; `protected:` can add paths but never remove these.

| Path | Why |
|---|---|
| `lockflight.yaml`, `dotnet-tools.json`, `.config/dotnet-tools.json` | the manifest and the engine pin |
| `.lockflight/**` | evidence, baselines, ledger and the linked engine |
| `.claude/settings.json`, `.claude/settings.local.json` | Claude Code settings that load the plugin and its hooks |
| `.claude/hooks/**`, `.claude/agents/**`, `.claude/skills/**`, `.claude/commands/**` | the team's own controls: a guard hook edited to exit 0 would be a silent bypass |
| `**/Directory.Build.props`, `**/Directory.Build.targets`, `**/Directory.Build.rsp`, `**/Directory.Packages.props`, `global.json` | .NET build control |
| `**/.editorconfig`, `**/.globalconfig`, `**/*.globalconfig`, `**/*.ruleset`, `**/stylecop.json`, `**/BannedSymbols.txt` | code-style and analyzer policy the build gate enforces |
| `**/NuGet.config`, `**/nuget.config` | package sources |
| `.gitleaks.toml`, `.gitleaksignore` | secret-scanning policy |
| `.github/workflows/lockflight-gates.yml` | the CI job that judges pull requests |

`init` and `adopt apply` also write an `Edit` deny rule for each protected path into `.claude/settings.json`, so Claude Code
refuses its own file tools there: see [permission deny rules](guardrails-and-boundaries.md#permission-deny-rules).

The protected paths that exist are also listed as Claude Code sandbox write denials in `.claude/settings.json`, where the
operating system enforces them for shell commands while the sandbox is on: see
[the Claude Code sandbox](guardrails-and-boundaries.md#the-claude-code-sandbox).

**In every unit** (`units:`), the root-anchored entries above are protected again under the unit's path: for example
`services/orders/global.json`, `services/orders/.claude/hooks/**` and `services/orders/lockflight.yaml`. Each unit's
`.github/workflows/**` is protected as a whole, because a child repository's own CI is its only hard gate.

## Gate profiles

| Profile | Steps | Typical use |
|---|---|---|
| `edit` | format (changed lines) | quick feedback |
| `stop` | format → build (no new warnings vs baseline) → test | end of an agent turn |
| `pr` | policy.protected → stop steps (+ coverage instrumentation) → coverage.patch → security.deps → security.secrets | CI, before a PR |

Verdicts and exit codes:

| | Verdict | Exit | Meaning |
|:---:|---|:---:|---|
| <img src="../assets/levi-pass.svg" alt="Levi raising a green flag" width="44"> | `PASS` | 0 | also a run in which every step was `N/A` |
| <img src="../assets/levi-fail.svg" alt="Levi raising a red flag" width="44"> | `FAIL` | 1 | |
| <img src="../assets/levi-blocked.svg" alt="Levi behind a lowered barrier" width="44"> | blocked | 2 | a hook, or a human-only command in an agent session |
| | `ERROR` | 3 | |
| | configuration | 4 | |
| <img src="../assets/levi-not-earned.svg" alt="Levi with a question mark" width="44"> | `NOT-EARNED` | 5 | nothing was judged — for example zero tests ran or gitleaks is missing; treat it as not passing |

A step can also be `N/A`: it does not apply to this run (coverage not configured or too few changed lines, no changed C# lines
for `format`, no `--base` for `policy.protected`). N/A neither passes nor blocks; the run's verdict is that of its last step
that applied. When **no** step applied (for example `edit` with no changed C# lines), the whole run is `N/A` and exits 0.

The `Scope:` line says what the run judged: the number of tests when the tests ran, otherwise what the last step that reports
a scope checked (for example `3 changed C# files (format)`), or `nothing judged`.

### The format step judges the change, not the file

`format` runs `dotnet format whitespace --folder --verify-no-changes --report <dir>` on the C# files the change touches, then
reads the report. It fails only on diagnostics whose replaced span changes a **changed line** (from `git diff -U0`, as
`coverage.patch` uses). Existing formatting debt elsewhere in a touched file is counted (`… not judged`) but does not fail
the step.

End-of-line differences are never attributed to a change: a span that differs from dotnet format's replacement only in
carriage returns comes from how git checked the file out (`core.autocrlf`, `.gitattributes`) against `.editorconfig`'s
`end_of_line`. The step says how many it skipped, once. Encoding differences (`CHARSET`: a byte order mark that disagrees with
`.editorconfig`'s `charset`) are attributed only to a file the change adds, or one whose byte order mark the change adds or
removes; an edit inside an existing BOM file is not blamed for its encoding. If the report cannot be read, the step judges
whole files and says so; if git cannot list the changed lines, it is NOT-EARNED. With `--base`, the changed lines include the
branch's commits since the merge base, so `--base` also scopes `format` (and `coverage.patch`).

## Baselines (brownfield adoption; human-only)

| Command | Accepts | File |
|---|---|---|
| `lockflight baseline build` | current build warnings | `.lockflight/baselines/build-diagnostics.json` |
| `lockflight baseline tests` | currently failing tests | `.lockflight/baselines/known-failing-tests.json` |
| `lockflight baseline deps` | current vulnerable-package advisories | `.lockflight/baselines/vulnerable-packages.json` |
| `.gitleaksignore` | known false-positive secrets (gitleaks' own format) | repository root |

## Lifecycle commands (standard profile)

| Command | Who | Purpose |
|---|---|---|
| `phase check specify\|tasks [--feature id]` | agent | deterministic phase-exit checks; for tasks, a failure names each task, its line and how it was classified (test, stub or implementation) |
| `approve spec\|tasks [--sign [--key file]]` | **human** | hash-bound approval, optional SSH signature |
| `approve verify (--feature id \| --base ref) [--allowed-signers file] [--require spec,tasks]` | CI | verify records, hashes, signatures |
| `redgreen start\|red\|green\|status` | agent | red/green cycles; tests locked while red; `red`/`green` refuse while the spec or tasks approval is stale or missing |
| `redgreen reset` | **human** | discard the feature's red/green evidence |
| `redgreen amend <test> --reason "…"` | **human** | let one test of the open red cycle change until the next red re-checks it |
| `trace [--feature id]` | agent | requirement → red-then-green tests matrix (Markdown) |
| `review [--base ref] [--model name] [--budget usd]` | agent | review (`claude -p`, read-only, schema); of record with the `review:` settings against the merge base of HEAD and the target |
| `review status [--feature id]` | agent | review runs, open, fixed and dismissed findings, and the pull request summary (read only) |
| `review dismiss <finding-id> --reason "…"` | **human** | close an open finding, recording who, when and why |
| `ledger retro [--days 30] [--min 2]` | anyone | recurring incidents and suggested remedies |

Evidence lives in `.lockflight/features/<feature>/` (approvals, `redgreen.json`, `reviews/` with one file per review run and
`reviews/dismissals/`) and is committed.

## Environment variables

| Variable | Effect |
|---|---|
| `LOCKFLIGHT_GITLEAKS` | path to the gitleaks binary (else PATH) |
| `LOCKFLIGHT_DEBUG` | hooks print path-matching diagnostics to stderr |
| `LOCKFLIGHT_HOOK_LOG` | append every raw hook payload to this file |

The engine sets these for every tool it starts (builds, test runs, git): `MSBUILDDISABLENODEREUSE=1` (no MSBuild servers left
behind), `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_NOLOGO=1`, `GIT_OPTIONAL_LOCKS=0` (read-only git calls take no index lock) and
`DiffEngine_Disabled=true` (a failed snapshot comparison in Verify or another DiffEngine-based library does not open a diff tool).

## Next

- [Commands](commands.md): every command and option
- [CI hardening](ci-hardening.md): how CI reads this policy from the base branch
- [Troubleshooting](troubleshooting.md): when a gate fails or says `NOT-EARNED`
