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
  target: .                     # directory the stop gate builds and tests: in a solution, the projects the
                                #   changes since its last PASS affect (see "The stop profile builds what the change affects")

gates:
  build: Shop.sln               # optional: the solution or project to build and test (default: the one in the target)
  timeoutSeconds: 600           # whole-run budget, 1–7200 s; child processes are killed when it runs out; it also
                                #   ends a redgreen red's FLAKY re-runs (counted from the red's start)
  test:
    framework: net10.0          # run tests for one target framework of multi-targeted projects
    stopFilter: "Category!=Integration"   # tests the stop gate and red/green's regression passes leave out (the pr
                                #   profile and CI run them); a unit's own test.stopFilter replaces it
  coverage:
    collector: none             # none | microsoft | coverlet  (patch coverage in the pr profile); every test project
                                #   must reference the collector package (doctor checks): on Microsoft.Testing.Platform
                                #   Microsoft.Testing.Extensions.CodeCoverage or coverlet.MTP, on VSTest
                                #   Microsoft.NET.Test.Sdk (built in) or coverlet.collector
    patchThreshold: 80          # % of changed coverable lines that tests must execute
    minChangedLines: 10         # below this many coverable changed lines, coverage is not judged (N/A)
  quality:
    suppressions: report        # fail | report | off (default report; fail in version 2): whether new suppressions fail the pr profile,
                                #   are only listed, or are not checked
  docs:
    references: report          # fail | report | off (default report; fail in version 2): whether references this change breaks in the
                                #   instruction files fail the pr profile, are only listed, or are not checked
  ef:
    pendingModelChanges: auto   # auto | fail | report | off (default auto): auto runs the check, and can fail, only
                                #   when a tool manifest pins dotnet-ef; fail runs it with any dotnet ef on the machine

units:                          # workspace shell or monorepo; omit for a single repository
  - path: services/orders       # relative to this file, no '..'; units cannot be nested
    build: Orders.sln           # optional: the solution or project to build (default: the one in the directory)
    kind: auto                  # auto | dotnet | none  (none: guarded, not built — e.g. a TypeScript or gitops repository)
    test:
      framework: net8.0         # this unit's test target framework
      stopFilter: "Category!=Integration"   # tests the stop gate and regression passes skip (pr still runs them)
  - path: web/admin
    kind: none

ci:                             # the CI judge (lockflight ci); read from the target branch only (all optional)
  maxRedCommits: 10             # the most red commits (Lockflight-Red trailer) one pull request may have, 1–50;
                                #   more is NOT-EARNED: split the pull request
  timeoutMinutes: 45            # the judge job's whole budget, 1–360 minutes
  flakyRetries: 2               # how often a test that fails at head is run again, 0–5
  lowRiskPaths:                 # a pull request that changes only these needs no red/green cycle, trace or approval
    - "docs/**"                 #   (default: docs/**, *.md at the root, .github/ISSUE_TEMPLATE/**)
  testInputs: []                # test inputs outside the test projects (testdata/ folders), compared like test files
  redCommits: true              # whether an accepted redgreen red commits the tree it judged

review:                         # what the review of record runs with (all optional)
  model: sonnet                 # Claude Code model name (default sonnet; opus to review with Opus)
  maxUsd: 3.00                  # the reviewer's --max-budget-usd, above 0 and at most 100 (default 3.00); on a
                                #   subscription an estimate at API prices, not a bill
  target: origin/main           # the branch the change targets (default: the branch origin/HEAD points at, else main)
  maxDiffCharacters: 120000     # the most diff characters the reviewer is shown, 10000 to 400000 (default 120000);
                                #   generated files are listed, not shown, and do not count
```

**Review of record.** A `lockflight review` run counts as the review of record only when it uses `review.model` and
`review.maxUsd` (or the defaults), the reviewer was shown the whole diff within `review.maxDiffCharacters`, its base commit is the merge base of HEAD and `review.target` (neither an older base nor a
recent commit on the branch, which would review only part of the change), and everything it reads is committed. The diff runs
without external diff tools or text conversions (`--no-ext-diff --no-textconv`), and a file git shows as binary whose content is
text (no NUL byte in its first 8,000 bytes, as git judges without attributes) makes the run not of record and is named for the
reviewer to read, so an attribute cannot hide a change from the review; a real binary file such as an image stays a one-line entry. The base defaults to the target. A run with `--model`, `--budget`, an outdated base or uncommitted or untracked files in the reviewed paths is still recorded, says why it is not of record, and
exits NOT-EARNED when it finds nothing.

**Diff limit.** `review.maxDiffCharacters` (default 120,000) is how much of the diff the reviewer is shown; a longer diff is
cut, the reviewer is told which files to read, and the run is not of record. Raise it for a change that cannot be split, up to
the engine's ceiling of 400,000 (a value outside 10,000 to 400,000 is a configuration error). A larger diff costs more tokens,
so `review.maxUsd` may need raising with it. Generated files (package lock files, EF Core model snapshots and migration
designer files, chosen by path and, for the EF files, by the shape of their content) are listed by path, size and hash instead
of shown and do not count toward the limit; see [`review`](commands.md). The run file records the limit under `settings`.
`lockflight.yaml` is a protected path, so the guard hooks refuse an agent's edit to the limit; that is a guardrail, not a
boundary. The boundary is on the server: the [CI workflow](ci-hardening.md) takes `lockflight.yaml` from the base branch, and
CODEOWNERS makes a change to it need a maintainer's review, so a pull request does not raise its own limit unseen.

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

Red/green and trace run xUnit v3 (natively or through Microsoft.Testing.Platform), xUnit v2 on VSTest, and NUnit and MSTest
on VSTest or Microsoft.Testing.Platform.

- **xUnit v3 runs:** natively, through the in-process runner the test assembly references; its version picks the report switch
  (`-xml` up to 3.x, `-result-xml` from 4.0), so both lines work.
- **xUnit v2 listing:** tests and their `Requirement` traits are read from the built assemblies' metadata, because VSTest's
  listing has neither.
- **xUnit v2 runs:** tests run with `dotnet test <dll> --filter`, and failures are read from the TRX. An assertion failure in a test
  that calls production code counts as RED_ASSERT, and a `NotImplementedException` thrown by production code as RED_STUB.
- **NUnit listing:** tests (`[Test]`, `[TestCase]`, `[TestCaseSource]`, `[Theory]`), their `[Ignore]` and `[Explicit]` marks and
  their `[Property("Requirement", "FR-001")]` values (on the method or its fixture) are read from the built assemblies'
  metadata. A `[Category]` is not a requirement: it has no name to say what its value is. Generic fixtures are left out.
- **NUnit runs:** a project that is a Microsoft.Testing.Platform application (`EnableNUnitRunner`) runs on it when it
  references `Microsoft.Testing.Extensions.TrxReport`, else through VSTest when it references `Microsoft.NET.Test.Sdk`; with
  neither, red/green says which to add. The tests are selected by an NUnit `Where` (class and method) in a run-settings file,
  so a parameterized fixture's instances run too, and failures are read from the TRX. NUnit prints no exception type for an
  assertion: its `Expected: … But was: …` lines (or `Multiple failures or warnings in test:`) count as an assertion, so a
  failing `Assert.That` in a test that calls production code is RED_ASSERT; `Assert.Fail` is RED_IN_TEST, and `Assert.Ignore`,
  `Assert.Inconclusive` or an ignored row is a test that did not run. Every `[TestCase]` row counts, the worst row deciding.
- **MSTest listing:** `[TestMethod]` methods (and `[DataTestMethod]` or other attributes derived from it) of public `[TestClass]`
  classes, their `[Ignore]` (on the method or the class) and their `[TestProperty("Requirement", "FR-001")]` values are read
  from the built assemblies' metadata. `[DataRow]` and `[DynamicData]` rows are one test. A `[TestCategory]` is not a
  requirement; internal test classes (`[assembly: DiscoverInternals]`) are left out.
- **MSTest runs:** on Microsoft.Testing.Platform (`EnableMSTestRunner`) when the project references
  `Microsoft.Testing.Extensions.TrxReport` (the `MSTest` metapackage brings it), else through VSTest when it references
  `Microsoft.NET.Test.Sdk`; tests are selected with
  `--filter "FullyQualifiedName=…"` and failures are read from the TRX. MSTest prints no exception type for an assertion: its
  wording (`Assert.AreEqual failed.` in MSTest 3, `Assertion failed. Expected …` in MSTest 4, `CollectionAssert`/`StringAssert`)
  counts as an assertion (RED_ASSERT when the test calls production code); `Assert.Fail` is RED_IN_TEST and
  `Assert.Inconclusive` a test that did not run.
- <a id="red-green-regression-passes"></a>**Regression passes.** `redgreen start` and `redgreen green` run the whole suite to
  see that nothing else broke; those runs leave out the tests `test.stopFilter` excludes (a unit's, else
  `gates.test.stopFilter`), so slow integration tests need not run at every green. The filter is applied only to those whole-suite
  runs: never to listing, to `redgreen red`, to the cycle's own tests (a green always runs them) or to an amend's re-check.
  - **Form:** one trait condition, `Name=Value` or `Name!=Value` (`Category!=Integration`; MSTest's categories are
    `TestCategory`, NUnit's `Category` or `TestCategory`), so the engine can tell from the listing which tests it leaves out:
    those count as neither missing nor skipped. Any other filter is not applied to red/green, which runs every test and warns.
    A known test that an earlier pass ran and this one leaves out (a category or filter added since) refuses the green.
    Categories the metadata does not show (inherited from a base fixture, set on the assembly or by a custom attribute) make
    the runner leave out a test the engine expects, and the green is refused as "missing from the run's report".
  - **Evidence:** `redgreen.json` records each pass's filter and the tests it left out (`baseline.regressionFilter`,
    `cycles[].regressionFilter`), and `redgreen status` shows the filter and the count.
  - **What still runs everything:** the `pr` gate profile and the CI check ignore `test.stopFilter`; the left-out tests are
    judged there before a merge.
- **Rows skipped at run time** (`Assert.Skip`, `Assert.Ignore`, an NUnit `[TestCase(…, Ignore = "…")]`) when `redgreen start`
  ran the suite are recorded with the baseline; a green refuses only more of them.
- **Other frameworks** (TUnit, for one) get the gates only.

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
- **`test.stopFilter`** (a unit's, else the root's `gates.test.stopFilter`) keeps slow tests out of the Stop gate and of
  red/green's regression passes, and leaves them to `pr`. On VSTest it is passed to `--filter` as is. On Microsoft.Testing.Platform
  only one trait condition is supported (`Category!=Integration` becomes `--filter-not-trait Category=Integration`).
- **Start directory:** start Claude Code at the shell root. Claude Code reads project settings only from the directory a
  session starts in, so a session started inside a unit loads neither the shell's settings nor its hooks.
  - With the plugin installed at user scope, Lockflight's guard still runs there, and the session banner says that the
    shell's settings did not load.

The **stop** profile's budget is `min(gates.timeoutSeconds, 540)`: the plugin's Stop hook is killed after 600 s
(`plugins/lockflight/hooks/hooks.json`), and the gate must end — and report — before that. A step that runs out of the capped
budget is ERROR and says so; a build and test run that needs longer belongs in CI (`gate run --profile pr`). Only the stop
profile is capped; `baseline tests` and the `pr` profile use the full `gates.timeoutSeconds`.

### Manifest version 2

`version: 2` adds the sections below. A manifest without `version`, or with `version: 1`, is read as version 2 with
**migration defaults**: the checks version 2 added (`gates.quality.suppressions`, `gates.docs.references`,
`approvals.check`) only report, so a new
engine never turns a green repository red by itself. In a version 2 manifest they default to `fail`. A version 1 manifest that
holds a version 2 section is an error naming the version it needs.

`lockflight init` writes version 2. A version 1 manifest is named to the person at session start and by `doctor`
(warn), and `lockflight update` migrates it (see [`update`](commands.md)); `update --project --dry-run` shows the change
first. A Lockflight engine before version 2 refuses a version 2 manifest ("'version' must be 1"): update every clone's
engine (`~/.lockflight/bin/lockflight update`) when the migration is merged.

The published JSON Schema, [`lockflight.schema.json`](../../lockflight.schema.json) (a copy of
[`schemas/lockflight.manifest.v2.json`](../../schemas/lockflight.manifest.v2.json)), gives an editor completion and checks.
Add this first line for editors that use the YAML language server:

```yaml
# yaml-language-server: $schema=https://raw.githubusercontent.com/lockflight/lockflight/main/lockflight.schema.json
```

The engine's reader is the authority. Rules a JSON Schema cannot express (globs that stay inside the project, a large change
larger than a fix, coverage that never decreases with the class, layers that do not overlap, units that do not nest, a shell URL
without credentials) are checked by the engine only, and the schema's descriptions name them.

```yaml
version: 2                      # required in this shape
requiresEngine: "0.3.0"         # optional: the lowest engine this manifest needs; an older engine says so before any
                                #   other error (an unknown key also names the engine version reading it)

routing:                        # how a pull request's change class is computed, and what each class must prove
  preset: default               # relaxed | default | strict; required. Keys left out take the preset's values, and
                                #   doctor names them: write the numbers out, a later engine may change a preset
  floor: fix                    # the lowest class any change is judged at: fix | behaviour | feature | critical
  fix: { maxLines: 20, maxFiles: 2 }   # larger than this is at least behaviour
  largeLines: 400               # larger than this is at least feature (must be above fix.maxLines)
  structural: critical          # a new project, a build-wide file or a solution change: feature | critical
  critical: ["src/Payments/**"] # globs of critical paths
  dbPaths: []                   # extra globs that mean "database", besides migrations and SQL files
  generatedPaths: []            # generated code: left out of size and shape
  nonProduction: []             # vendored code, samples, tools: left out of size and shape
  requiredWorkflows: []         # CI files a platform ruleset requires: changing one is critical
  apiProjects: []               # project directories whose public surface is an API
  layers:                       # optional, at least two, inner first, globs of different layers never overlap
    - { name: domain, paths: ["src/*.Domain/**"] }
    - { name: infrastructure, paths: ["src/*.Infrastructure/**"] }
  coverage: { fix: 80, behaviour: 80, feature: 80, critical: 90 }   # patch coverage per class, never decreasing
  require:
    specFrom: feature           # a spec is required from this class: behaviour | feature | critical
    reviewFrom: behaviour       # the review runs (with an API key) from this class
    reviewKeyFrom: critical     # without a key, NOT-EARNED from this class (not below reviewFrom), or none
    architectureFrom: feature   # declared architecture tests are required from this class
    architectureDeclaredFrom: none   # critical | none: from this class, no declared architecture tests is NOT-EARNED

approvals:                      # the approval of record is the platform's review of the pull request
  check: fail                   # fail | report: whether a missing approval fails the CI check or is only listed
  default: same-pr              # same-pr | spec-first: the spec arrives with the code, or is merged first
  specFirstFrom: none           # none | feature | critical: from this class spec-first applies anyway
  specFirstPaths: []            # a change touching these globs needs spec-first
  specPaths: ["specs/**"]       # what counts as the spec, for the CODEOWNERS coverage check (never empty)

autonomy:                       # reserved: validated, no effect in this version
  maxClass: fix
  budgetUsd: 5.00
  timeMinutes: 60

shell:                          # this repository is a unit of a workspace shell (not together with units:; init --unit-of writes it)
  url: git@github.com:example/shell.git   # https:// or SSH; never with credentials (use a deploy key or a CI secret)
  ref: main                     # optional: the shell's branch (default: its default branch)
  unit: services/orders         # this repository's path among the shell's units
```

| Preset | `floor` | `fix` lines / files | `largeLines` | coverage fix / behaviour / feature / critical | `specFrom` | `reviewFrom` | `reviewKeyFrom` | `architectureFrom` | `architectureDeclaredFrom` | `approvals.specFirstFrom` |
|---|---|---|---|---|---|---|---|---|---|---|
| `relaxed` | fix | 40 / 3 | 800 | 70 / 70 / 70 / 80 | critical | feature | critical | critical | none | none |
| `default` | fix | 20 / 2 | 400 | 80 / 80 / 80 / 90 | feature | behaviour | critical | feature | none | none |
| `strict` | behaviour | 10 / 1 | 200 | 85 / 85 / 90 / 95 | behaviour | behaviour | feature | behaviour | critical | critical |

Every preset sets `structural: critical`. How each `routing:` key is used, signal by signal, is in
[risk routing](risk-routing.md#signals).

> [!IMPORTANT]
> In this version `routing:` and `approvals:` are read and validated, `doctor` reports on them, and `route show` computes a
> change's class and requirements from `routing:`, but no check judges a change by them yet. `autonomy:` is reserved and has no effect. None of them is a control today; the descriptions above say
> what they will decide.

### Built-in protected paths

These always apply; `protected:` can add paths but never remove these. The guard blocks the agent's edits to them and the
common shell writes it parses (redirections, `tee`, `cp`/`mv`, `sed -i`, `rm`, `git checkout`/`restore`, `dotnet format`, the
PowerShell equivalents); a script, an interpreter or another command whose writes it does not parse is allowed. That is a
guardrail against accidents: the CI `policy.protected` step, with the base branch's policy, is where a change to these paths is
judged. See [what the shell write detection sees](guardrails-and-boundaries.md#what-the-shell-write-detection-sees).

| Path | Why |
|---|---|
| `lockflight.yaml`, `dotnet-tools.json`, `.config/dotnet-tools.json` | the manifest and the engine pin |
| `.lockflight/**` | evidence, baselines, ledger and the linked engine |
| `.claude/settings.json`, `.claude/settings.local.json` | Claude Code settings that load the plugin and its hooks |
| `.claude/hooks/**`, `.claude/agents/**`, `.claude/skills/**`, `.claude/commands/**` | the team's own controls: a guard hook edited to exit 0 would be a silent bypass |
| `**/Directory.Build.props`, `**/Directory.Build.targets`, `**/Directory.Build.rsp`, `**/Directory.Packages.props`, `global.json` | .NET build control |
| `**/.editorconfig`, `**/.globalconfig`, `**/*.globalconfig`, `**/*.ruleset`, `**/stylecop.json`, `**/BannedSymbols.txt`, `**/CodeMetricsConfig.txt` | code-style and analyzer policy the build gate enforces |
| `**/.gitattributes` | what git diffs show: a `-diff` or `binary` line, or a diff driver, can show a text change as "Binary files … differ" (attributes set outside the tree, in `.git/info/attributes` or `core.attributesFile`, are not covered; the review's own check below is) |
| `**/NuGet.config`, `**/nuget.config` | package sources |
| `.gitleaks.toml`, `.gitleaksignore` | secret-scanning policy |
| `.github/workflows/lockflight-gates.yml`, `.azure-pipelines/lockflight-gates.yml`, `.gitlab/ci/lockflight-gates.yml` | the CI job that judges pull requests |
| `.gitlab-ci.yml`, `bitbucket-pipelines.yml` | the one pipeline file that decides whether the job runs at all on GitLab and Bitbucket: see [CI on other platforms](ci-other-platforms.md) |

`init` also writes an `Edit` deny rule for each protected path into `.claude/settings.json`, so Claude Code
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
| `stop` | format → projects.affected → build (no new warnings vs baseline) → test, of the projects the change affects | end of an agent turn |
| `pr` | policy.protected → format → quality.suppressions → docs.references → build → ef.pendingModelChanges → test (+ coverage instrumentation) → coverage.patch → security.deps → security.secrets | CI, before a PR |

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

### The stop profile builds what the change affects

In a solution, the stop profile builds and tests only the projects changed since the target's last stop PASS and the projects
that depend on them, transitively. The `pr` profile and CI always build and test everything, so this shortens the Stop gate
without narrowing the definition of done.

- **Changed since the last PASS:** every stop PASS of a target records its working tree as a git tree object
  (`.lockflight/local/affected/<target>.pass`, written through a private index; the person's index is untouched). The next
  run compares the working tree with it, so edits, commits made without a stop run, reverts, deletions and new files all
  count.
- **The graph:** `projects.affected` reads the solution (`.sln` or `.slnx`: the `build:` file, else the only one in the target)
  and each project file's `ProjectReference` items as text, without MSBuild, following its imports and the
  `Directory.Build` and `Directory.Packages` files above it. A changed file belongs to every project whose directory holds it
  (an outer project of a nested one too, and projects that share a directory), and to a project that includes it from
  outside its own directory (a linked file, an imported shared project).
- **What runs:** the changed projects and every project that references one of them, through a solution filter
  (`.lockflight/local/affected/<target>.slnf`). Their project references still build. The tests are the test projects among
  them; when none is a test project, `test` is N/A and says so.
- **What it records:** the step's detail, in the result and in the ledger, names each project and why, for example
  `Api.Tests.csproj (affected by src/Domain/Order.cs via Api.csproj)`.
- **Changes that choose nothing:** files in another unit of the workspace, and Lockflight's own directories (`.lockflight/`,
  `.claude/`, `.specify/`, `specs/`).
- **Everything runs** (the detail says why) when the choice could miss a project:
  - a shared build input changed: any `Directory.Build.*`, `Directory.Packages.props`, `global.json`, `NuGet.config`,
    `.props`, `.targets`, solution, `.editorconfig` or `.globalconfig`;
  - a file changed outside every project in the target, or a source or project file outside the target that no project holds;
  - the graph cannot be settled from the text: a reference, include or import through an MSBuild property other than the
    file's or the project's own directory, a wildcard or missing reference, an imported or inherited file that adds project
    references or lies outside the repository (a unit repository under a shell's `Directory.Build.props`), a project type
    other than C#, F# or Visual Basic, or a project outside the repository or in a nested one;
  - no affected project declares tests (in its file or a file it imports) while a `Directory.Build` or `Directory.Packages`
    file names a test framework, so test projects cannot be told apart;
  - an input git does not see: a git-ignored file a project imports or includes (a `local.props`), or an index entry marked
    assume-unchanged or skip-worktree (a sparse checkout);
  - there is no stop PASS of the target yet under this engine, these settings and the SDK the target resolves, nothing
    changed since it, there is no
    single solution, git fails, or the changes affect every project;
  - `gate run --full`.
- **The cache:** a PASS is remembered under the inputs hash as before. The choice is a function of what that hash covers
  (HEAD, the uncommitted changes and new files, the settings) and of the recorded tree of the last PASS, which only a PASS
  writes and which already passed; the step's version is part of the key, so a PASS chosen by other rules is not reused.

The choice is made per unit in a workspace, inside the unit's solution, after the unit itself was chosen because it changed.
A monorepo unit whose projects reference or include files in another unit is not chosen when only that other unit changes
(the unit's state covers its own directory and the shared build inputs outside every unit); CI's `pr` run builds both.

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

### New suppressions fail the pr profile

`quality.suppressions` reads the lines the change adds (with `--base`, everything since the merge base; without, the uncommitted
changes and untracked files) and lists each suppression it finds, with file, line and rule:

| Added line | Rule it names |
|---|---|
| `#pragma warning disable CA1822` (C#) | each listed id; `all` when none is listed |
| `[SuppressMessage("…", "CA1822:…")]`, also `[assembly: …]` in `GlobalSuppressions.cs` and `[UnconditionalSuppressMessage]` | the check id |
| `<NoWarn>`, `<WarningsNotAsErrors>` (a property, or `NoWarn="…"` on an item) in a project, `.props` or `.targets` file | each listed id; `$(NoWarn)` is not one |
| `dotnet_diagnostic.<id>.severity` (or a category, or all analyzers) in `.editorconfig` or a `.globalconfig`, set below `warning` or below what the line said before | the id, `category-<name>` or `all` |
| `#nullable disable` | `nullable` |
| `[ExcludeFromCodeCoverage]` | `ExcludeFromCodeCoverage` |

Suppressions already in the code are not judged, and one moved within its file is not new. Comments and strings are not read
as code. Generated files are left out by path and shape (package lock files, EF Core model snapshots and migration designer
files, as the review leaves them out), and so is the `#nullable disable` EF Core writes at the top of a migration whose designer
is EF Core's. A person accepts a needed suppression with `lockflight baseline suppressions`, by file and rule: that rule may then
be suppressed again in that file, any other file or rule still fails.

`gates.quality.suppressions` decides what a finding does: `fail` fails the run, `report` lists the findings on a `PASS`
(`report only …`), `off` skips the step (`off by manifest`). An `ERROR` (git could not diff) is an `ERROR` in every mode. The
default is `report`, so upgrading the engine never turns a green repository red by itself; set `fail` to make it a gate. This is
a line scan, not a compiler: a suppression it cannot see (a directive inside an inactive `#if` region is still read; a
`<NoWarn>` value split over lines is not) is a known gap.

### References the change breaks in the instruction files

`docs.references` reads the files the agent takes instructions from: every `CLAUDE.md` and `AGENTS.md`, and the Markdown under
`.claude/rules/`, `.claude/skills/`, `.claude/agents/` and `.claude/commands/`. It runs the reference checks of
[`docs check`](commands.md#setup) on them (links and heading anchors, code-span paths that history deleted or
renamed, engine and plugin command lines) twice: on the working tree, and on the comparison commit — the merge base with
`--base`, else the last commit — read through git, without a checkout. A reference broken now but not then fails, with file,
line, reference and reason; one that was already broken is listed (`broken before this change, not judged`) and left to you.
A reference is the same when its file, text and kind are, wherever its line moved, so renaming an instruction file makes its
broken references new. `docs check` itself stays advisory and also reads `docs/` and `README.md`; this step does not.

`gates.docs.references` takes `fail`, `report` (the default: findings listed on a `PASS`) or `off`, as
`gates.quality.suppressions` does; an `ERROR` (an unknown base, a shallow checkout) stays an `ERROR`. Locally the step is a guardrail; in CI it is a
boundary only with `fail`, a required check and the manifest taken from the base branch. What it checks is what `docs check`
checks: a path the repository never had is not reported, and a code-span path counts only with evidence from history.

### EF Core model changes need a migration

`ef.pendingModelChanges` runs per unit, after the build, for each DbContext with migrations: a `*ModelSnapshot.cs` in a
`Migrations` folder names the context, and the nearest project file above it is its project. It checks a context only when
the change touches its project or a project it references (since the merge base with `--base`, plus uncommitted and untracked
files), and then runs `dotnet ef migrations has-pending-model-changes --project … --context …` (EF Core 8 or later), with the
project itself as the startup project when it references `Microsoft.EntityFrameworkCore.Design`, else the first project of
the unit that references both that package and this project. A model that changed without a migration fails, naming the
project and context; `dotnet ef` failing for any other reason is an `ERROR`; the runs share the gate's time budget.

With `auto` (the default) it runs only when a tool manifest (`dotnet-tools.json` or `.config/dotnet-tools.json`, in the
unit or above it up to the project root) pins `dotnet-ef`, after `dotnet tool restore`; otherwise it is `N/A` and says so. A
global `dotnet ef` is not the repository's choice, so it runs only with `fail`. `report` lists a pending change on a `PASS`,
`off` skips the step. The project files are read as text, not evaluated by MSBuild: a reference added through an imported
file is not seen.

## Baselines (brownfield adoption; human-only)

| Command | Accepts | File |
|---|---|---|
| `lockflight baseline build` | current build warnings | `.lockflight/baselines/build-diagnostics.json` |
| `lockflight baseline tests` | currently failing tests | `.lockflight/baselines/known-failing-tests.json` |
| `lockflight baseline deps` | current vulnerable-package advisories | `.lockflight/baselines/vulnerable-packages.json` |
| `lockflight baseline suppressions` | the suppressions the branch adds since it left the default branch, by file and rule, added to those accepted before | `.lockflight/baselines/suppressions.json` |
| `.gitleaksignore` | known false-positive secrets (gitleaks' own format) | repository root |

## Lifecycle commands (standard profile)

| Command | Who | Purpose |
|---|---|---|
| `phase check specify\|tasks [--feature id]` | agent | deterministic phase-exit checks; for tasks, a failure names each task, its line and how it was classified (test, stub or implementation) |
| `approve spec\|tasks [--feature id]` | **human** | hash-bound approval record (local) |
| `approve verify (--feature id \| --base ref) [--require spec,tasks]` | CI | verify records and hashes |
| `redgreen start\|red\|green\|status` | agent | red/green cycles; tests locked while red; `red`/`green` refuse while the spec or tasks approval is stale or missing |
| `redgreen reset` | **human** | discard the feature's red/green evidence |
| `redgreen amend <test> --reason "…"` | **human** | let one test of the open red cycle change until the next red re-checks it |
| `trace [--feature id]` | agent | requirement → red-then-green tests matrix (Markdown) |
| `review [--base ref] [--model name] [--budget usd]` | agent | review (`claude -p`, read-only, schema); of record with the `review:` settings against the merge base of HEAD and the target |
| `review status [--feature id]` | agent | review runs, open, fixed and dismissed findings, and the pull request summary (read only) |
| `review dismiss <finding-id> --reason "…"` | **human** | close an open finding, recording who, when and why |

Evidence lives in `.lockflight/features/<feature>/` (approvals, `redgreen.json`, `reviews/` with one file per review run and
`reviews/dismissals/`) and is committed.

## Environment variables

| Variable | Effect |
|---|---|
| `LOCKFLIGHT_GITLEAKS` | path to the gitleaks binary (else PATH) |
| `LOCKFLIGHT_DEBUG` | hooks print path-matching diagnostics to stderr |
| `LOCKFLIGHT_HOOK_LOG` | append every raw hook payload to this file |
| `LOCKFLIGHT_HOOK_ADAPTER` | which client's payload `lockflight hook` reads: unset or `claude-code` (the plugin), `neutral` (a `lockflight.hook-request/v1` document, experimental). Another value blocks a tool call |

The engine sets these for every tool it starts (builds, test runs, git): `MSBUILDDISABLENODEREUSE=1` (no MSBuild servers left
behind), `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_NOLOGO=1`, `GIT_OPTIONAL_LOCKS=0` (read-only git calls take no index lock) and
`DiffEngine_Disabled=true` (a failed snapshot comparison in Verify or another DiffEngine-based library does not open a diff tool).

## Next

- [Commands](commands.md): every command and option
- [CI hardening](ci-hardening.md): how CI reads this policy from the base branch
- [Troubleshooting](troubleshooting.md): when a gate fails or says `NOT-EARNED`
