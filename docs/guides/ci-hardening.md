# CI hardening

The agent works with your credentials, so CI is where an independent check happens. `lockflight init` writes
`.github/workflows/lockflight-gates.yml`; this page explains what it does and what you add by hand. On Bitbucket
Pipelines, `init --ci-platform bitbucket` writes the judge as a pipeline template (not yet run on a live workspace); on GitLab
CI/CD and Azure Pipelines, `init --ci-platform gitlab|azure` writes a job that runs the `pr` gates only (experimental): see
[CI on other platforms](ci-other-platforms.md).

**On this page:** [What the generated workflow does](#what-the-generated-workflow-does) ·
[Which check is required](#which-check-is-required) · [Moving from 0.2](#moving-from-lockflight-02s-gates-workflow) ·
[What the CI judge shows](#what-the-ci-judge-shows-and-what-it-does-not) · [The CI judge action](#the-ci-judge-action) ·
[CODEOWNERS for the protected paths](#what-you-add-codeowners-for-the-protected-paths) ·
[Approvals of record](#approvals-of-record-the-platforms-review) ·
[Enable workflows on a new repository](#enable-workflows-on-a-new-repository) · [Plan limits](#plan-limits) · [Locally](#locally) ·
[Workspace shells](#workspace-shells-unit-repositories)

## What the generated workflow does

- **Four jobs.** `judge` runs the [CI judge action](#the-ci-judge-action) on the pull request's head commit with no secret;
  `review` runs the review of record only when the repository variable `LOCKFLIGHT_REVIEW` is `'true'` (and the secret
  `ANTHROPIC_API_KEY` is set), for pull requests from the same repository, and not after a judge FAIL; `gates` and
  `lockflight/judge` read the results as data and set one status context each. `gates` keeps the name and meaning of the
  check rulesets already require. The judge job's `timeout-minutes` is `ci.timeoutMinutes` (45) plus 10: raise both together.
- **Policy from the target branch.** The judge reads `lockflight.yaml` from the target branch's tip with `git show`, and lays
  exactly these from that commit over every tree it builds (a file the PR adds is removed):
  - `.lockflight/engine.lock` (the engine pin by hash) and `dotnet-tools.json` / `.config/dotnet-tools.json` (its version
    mirror, and the version pin of a project without a lock);
  - `lockflight.yaml`;
  - `.gitleaks.toml` and `.gitleaksignore`;
  - `nuget.config`, `NuGet.config` and `NuGet.Config` at the root (package sources: a PR cannot make CI install an engine of its own choosing);
  - `.lockflight/baselines/` (accepted warnings, known-failing tests, advisories) and `.lockflight/ci/` (the vendored engine
    install script);
  - code-style and analyzer config anywhere in the tree: `.editorconfig`, `.globalconfig`/`*.globalconfig`, `*.ruleset`,
    `stylecop.json`, `BannedSymbols.txt`, `CodeMetricsConfig.txt`. A PR is judged by the base's style rules, and its own rule changes apply after the
    merge.

  A PR cannot change the policy it is judged with, pre-accept its own secret or accept its own failing test through a
  baseline. It can still affect the results, as the next section says. On `pull_request`
  events GitHub runs the **PR's** copy of the workflow file itself, so this holds against a hostile PR only together with the
  CODEOWNERS rule and the required check below.

  It does **not** restore what the build itself needs in the PR's version: `Directory.Build.*`, project files,
  nested `NuGet.config` files, `global.json` and `Directory.Packages.props`. A PR can change these, and the gates then build with the
  changed version. For example, a PR could set `<TreatWarningsAsErrors>false</TreatWarningsAsErrors>` in
  `Directory.Build.props`. `policy.protected` lists these files for review. **CODEOWNERS is what protects them.**
- **Pinned tools.** The action is referenced by commit SHA. gitleaks is downloaded at a fixed version and verified against its
  SHA-256 before use. The engine is the one the target branch pins, read from that commit with `git show` (never from the pull
  request's tree) and installed outside the repository, so a `global.json` that pins SDK 8 or 9 does not matter:
  - **With `.lockflight/engine.lock`:** the vendored `.lockflight/ci/install.sh` is checked against the hash the lock names,
    then run: it downloads this platform's package (nuget.org, or through `dotnet` with the root `nuget.config` when that file
    exists) and checks the executable against the lock before anything runs it.
  - **Without a lock** (a project pinned the way Lockflight 0.2 pins): the version in the tool pin, with
    `dotnet tool install --tool-path`.
  - **A pull request that changes the lock:** the judge job runs `lockflight engine verify-lock` on the proposed lock with the
    base's engine; it fails unless every platform's hash and the install script's hash are the ones the release of that version
    published, unless the pull request's `.lockflight/ci/install.sh` is the script the proposed lock pins (`--script`), and unless
    the release workflow's attestation verifies (the workflow's token). A lock that moves the engine to an older
    version is named in the verdict ("moves the engine down"), so a downgrade is accepted only on purpose.
  - **The first lock** (the pull request that moves a 0.2 tool pin to the lock): the base's 0.2 engine has no `verify-lock`, so
    the action downloads the GitHub Release's `engine-hashes.json` for the lock's version, verifies its attestation with `gh`,
    and compares every hash, the install script's included, and the vendored `.lockflight/ci/install.sh` with it. It reads the
    lock as strictly as the engine does: an unknown or repeated key, or any backslash (which `install.sh`'s reader would not
    decode as JSON does), is refused. A failure is an error annotation that names the check as `engine verify-lock` does
    (`engine lock: FAIL hash-mismatch: …`). Without a token nothing can be checked, so the step fails with
    `engine lock: FAIL unattested`, and keeps failing until it has one: leave the judge step's `github-token` input at its
    default, the workflow's token (`contents: read` is enough), and re-run.
  - **When the lock is refused** (a changed or first lock, or a pull request that removes the lock: `lock-removed`), the judge
    does not run. The step leaves a short evidence instead (`refused.json`, `lockflight.ci-evidence/v1` with `judge`
    `refused`), and the report shows FAIL with the reason `ENGINE_LOCK_REFUSED` and the failed check, rather than "the judge
    did not finish". An engine older than this reads no such file and still says the judge did not finish. On Azure, Bitbucket and GitLab the first lock is **not** release-checked: the job prints a warning, and
    the person accepting the pull request runs `lockflight engine verify-lock .lockflight/engine.lock` first.
- **Trigger.** On every pull request and push to `main` (`init --ci pr`), or only when started by hand (`--ci manual`). An
  engine pin that is a development build is on no package feed, so `init` writes the manual trigger for it unless you pass
  `--ci pr`; see [CI with a development build](getting-started.md#ci-with-a-development-build). On a new
  repository, [enable workflows](#enable-workflows-on-a-new-repository) before the first pull request.
- **`gate run --profile pr --base <merge base>`**, run by the judge at head in its worktree, in this order:
  1. `policy.protected` — lists protected files the PR changes (never fails; the list goes to the job summary);
  2. `format`, then `quality.suppressions` — suppressions the PR adds (`#pragma warning disable`, `[SuppressMessage]`,
     `<NoWarn>`, a lowered `.editorconfig` severity, `#nullable disable`, `[ExcludeFromCodeCoverage]` …), by file, line and
     rule; it fails only with `gates.quality.suppressions: fail` (the default, `report`, lists them). A needed one is accepted
     with `baseline suppressions`, merged in its own pull request like every baseline. With the manifest and the baseline taken
     from the base branch and the check required, a PR cannot switch the step off or accept its own suppressions;
     then `docs.references` — links, anchors, code-span paths and command lines in `CLAUDE.md`, `AGENTS.md` and `.claude/`
     that this PR broke, compared with the merge base (references broken before are listed, not judged); it fails only
     with `gates.docs.references: fail`. It needs the full history (`fetch-depth: 0`, as the workflow checks out);
  3. `build`, then `ef.pendingModelChanges` — when the repository pins `dotnet-ef` in its tool manifest and the PR touches
     an EF Core project, `dotnet ef migrations has-pending-model-changes` for each of its DbContexts: a model changed without
     a migration fails (N/A without the pin, unless `gates.ef.pendingModelChanges: fail`), then `test` (with coverage when
     `gates.coverage.collector` is set);
  4. `coverage.patch` — covered share of the changed coverable lines ≥ `patchThreshold` (default 80 %; N/A below
     `minChangedLines`, default 10, and when `gates.coverage.collector` is not set). A deterministic CI build
     (`ContinuousIntegrationBuild`) may leave `/_/…` source paths in the report; they are read against the repository the
     tests were built in;
  5. `security.deps` — no new High/Critical advisories;
  6. `security.secrets` — gitleaks over the PR's commits, uncommitted changes, and untracked files that git does not ignore.
     False positives go to `.gitleaksignore`, reviewed by a human. An added inline `gitleaks:allow` comment fails the step.
     In the CI judge this scan runs first, before anything of the pull request is built (see
     [what the CI judge shows](#what-the-ci-judge-shows-and-what-it-does-not)), and the gates report
     its result here.
     The engine's own recorded hashes are not findings: a generic-key hit in a red/green, approval or review record under
     `.lockflight/features/<id>/` whose whole match is a JSON name and a bare 40- or 64-digit lowercase hex value (a commit id
     or SHA-256). Everything else in those files is scanned as usual.

Only the PR's own commits are scanned for secrets, so existing history in a brownfield repo does not block adoption.

> [!IMPORTANT]
> **Baselines go in their own pull request.** Because the workflow takes `.lockflight/baselines/` from the base branch, a
> baseline committed in a feature's pull request is discarded when that pull request is judged, by design. When a brownfield
> repository has tests that already fail (or warnings, or advisories) that a feature should not own, type
> `/lockflight:baseline tests` (or `build`, `deps`) in Claude Code, or, in a separate terminal window, run
> `.lockflight/bin/lockflight baseline tests` yourself; merge that baseline
> in a separate pull request first, then continue the feature. `redgreen start` warns when tests already fail before the
> feature changes anything.

**Multi-targeted test projects.** `init` reads the test projects' `TargetFramework(s)` and makes `setup-dotnet` install every
.NET channel they need (for example `8.0.x` and `10.0.x`); a workflow that already existed is left alone, and `init` says what
it should install.

## What the CI judge shows, and what it does not

Every check that does not pass names a reason code (`GREEN_BEFORE`, `TRACE_UNCOVERED`, …); the PR comment lists each one
under the checks table with what to do next and a link to its entry in [reasons](reasons.md).

To read a verdict outside the pull request, `lockflight ci explain <run URL or id>` fetches the run's verdict with the GitHub
CLI (`gh run download`, signed in with `gh auth login`) and lists each check that did not pass with its detail, reason code,
Next line and link; it also reads a downloaded `lockflight-verdict` or `lockflight-evidence` artifact folder, and, without an
operand, the local judge's last run. In Claude Code, the plugin's `explain-ci` skill runs it and fixes what is the agent's to
fix ([commands](commands.md#ci)).

`lockflight ci` re-runs the red/green evidence instead of trusting it. For each commit with a `Lockflight-Red: <cycle>`
trailer it builds that commit and requires its new tests to fail inside production code; it then requires every cycle test to
be the same test at head (or to fail again on the red commit's code when it changed) and to pass there, the spec's
requirements to be traced to those tests, and the `pr` gates to pass, all with the target branch's policy.
A requirement marked `[outside .NET: <path>]` is left out of trace while each file it names exists at head and contains its
ID; the judge runs none of those tests. When trace is evaluated (the pull request changes production code), the comment's
headline counts such requirements, each file's evidence says whether the pull request added or changed it, and a marker added
to, changed on or removed from a requirement the base already had is called out above the checks; a pull request that changes
only the spec shows it in the spec diff alone. The headline count and the call-out come from the judge's evidence, so they are
as strong as that evidence ([requirements verified outside .NET](spec-contract.md#specmd)).
A requirement marked `[structural]` is covered by a test, new in the pull request, naming only structural requirements that
passes at head and on the fork point's code with its own file and the test side's non-C# files laid over it (or, for an
architecture test calling no production code, fails there on an assertion); such tests are not part of a red commit's cycle.
This shows the test's assertions held on both sides, not that they pin the requirement; that is for review. Local trace checks them on the commit `redgreen start` recorded
instead of the fork point, so the two can differ when the branch's history moved.
A pull request whose spec is all `[structural]` needs no red commit when a person gates the spec (changed here under the target
branch's CODEOWNERS, or merged first under spec-first) and no routing signal carries behaviour: red is N/A, trace answers for every
requirement and refuses a new test that is not structural, and `tests.existingChanged` is required and runs the base's version of
each changed existing test on the head's code. Whether it qualifies is decided from git, and the report job decides it again; the
markers are agent-writable, so the claim rests on the spec's review, a boundary only where the platform requires code-owner
review on the spec path ([structural-only change](spec-contract.md#specmd)).

- **Read from the target branch** (a boundary, with the judge a required check and a required code-owner review covering
  the workflow files): the policy and baselines the judge applies, the engine the report jobs install, and that the check
  runs at all. Which commits and tests it examined reaches the report jobs through the evidence file, which the next points
  limit.
- **A strong guardrail, not a boundary:** the test results. The tests are the pull request's code and run in the judge job,
  so a determined pull request can forge their outcome. Doing so needs deliberate code in the test assemblies that tampers
  with the test host, which is visible in the test diff; the host-tampering check lists the plain forms of it and is
  NOT-EARNED when one is new. Each new finding comes with its entry for `.lockflight/baselines/host-tampering.json`
  (in `lockflight ci`'s output, the evidence's `baselineEntry` and the pull request comment): accepting legitimate code,
  such as a snapshot store that writes its case files in record mode, is a person's decision, made in a pull request of
  its own to the target branch, where the protected file shows as a protected change. Once it is merged, push to the
  pull request or update it from the target branch: re-running the old check judges against the old base.
- **A strong guardrail, not a boundary:** the evidence file itself. In 0.3.0 the judge writes its evidence in the same job
  as the pull request's code, so a process that code leaves running could rewrite it before the report jobs read it. On
  Linux and Windows the judge stops the processes it can find right before it writes the evidence (see below); one started
  outside the engine's tree, or as root, is not found. Isolation (a container or a separate job for the pull request's code) is planned after 0.3.0.
- **What the pull request's code can affect in the judge job.** The pull request's code runs as the same user as the engine,
  in the same job, before the judge has finished. On GitHub-hosted runners that user has administrator rights (passwordless
  `sudo` on Linux and macOS, an administrator account on Windows), so the code can act as root on the runner's VM:
  - leftovers in `obj/` and `bin/` carry from one build to the next in the judge's worktree;
  - git hooks the pull request's code installs can run when the judge checks out paths;
  - the policy and baselines are laid over the worktree from the target branch before each build, but are read there after
    the pull request's code has built and run in it;
  - the secrets scan runs before any of it: the judge checks out the head with the target branch's `.gitleaks.toml` and
    `.gitleaksignore` and scans it before the first build, and the `pr` gates report that scan instead of scanning again
    after the build. The action takes the SHA-256 of the gitleaks binary it installed, outside the checkout and before any of
    the pull request's code runs, and passes it as `LOCKFLIGHT_GITLEAKS_SHA256`; the engine copies the binary into a private
    temporary folder (readable by its own user only), hashes the copy, runs only the copy and deletes it after the scan, and
    refuses a binary that differs (ERROR, `SCANNER_REPLACED`). Replacing the installed binary after the check therefore
    changes nothing. In the judge this all happens before the pull request's code runs. In the CI files that run the gates
    alone (Azure Pipelines, GitLab CI/CD, and a unit repository's workflow) the `pr` gates build and test the pull request's
    code before they scan, so a process it left running as the same user could still write to the copy between
    the hash and the start: there the check is a guardrail. Everywhere it is still a guardrail against code with root on the
    VM, which could interfere with the engine itself;
  - the engine starts every build with the .NET build servers off, so the project's analyzers and source generators do not
    stay alive in a compiler server after its build, and runs `dotnet build-server shutdown` after each phase of the judge.
    Its `dotnet build` and `dotnet test` runs pass `-p:UseSharedCompilation=false -p:UseRazorBuildServer=false` as global
    properties, which a project's own files (a `Directory.Build.props`) cannot set back, and its builds also pass
    `--disable-build-servers`; every process it starts gets the same settings as environment variables, beside
    `DOTNET_CLI_USE_MSBUILD_SERVER=0` and `MSBUILDDISABLENODEREUSE=1`. A tool that builds by itself (`dotnet ef`) has only
    the environment variables, which a project can override, and an `<Exec>` can start any process it likes;
  - test-framework extension points (an xUnit v3 `TestFramework`, an NUnit `IWrapTestMethod` attribute, a custom runner)
    run inside the test host;
  - an MSBuild `<Exec>` task or inline task in a project, props or targets file runs arbitrary code at build time;
  - the host-tampering scan reads the test assemblies only: production code and MSBuild files are not scanned;
  - processes the pull request's code starts and leaves running (a daemon an `<Exec>` starts, a process a test forgets) are
    looked for, stopped and listed, but they do not change a verdict yet. Whenever the engine has none of its own child
    processes running (after each build, test run, git call and gate step), it finds every process its children started that
    is still alive, kills it, and records it in the evidence's `processes` object (where it was found, the step it outlived,
    its command line, and whether it was stopped) and in `lockflight ci`'s output. On Linux a process that exited by
    itself just before the check (an MSBuild worker node a moment after its build) is reaped and listed as `exited`. On Linux the engine makes itself a child
    subreaper, so a daemon that detaches (a double fork, `setsid`, `nohup`) is re-parented to the engine and found; on
    Windows the engine runs in a job object, which every process it or its descendants create joins. macOS is not checked
    yet (`mechanism: none`). A guardrail, not a boundary: code with the runner's root or administrator rights can start a
    process outside the engine's tree (`sudo systemd-run`, a scheduled task, WMI, a Docker container, which the runner's
    user can start without `sudo`) or one the engine cannot kill (a `sudo` child), and can change files a later step runs
    without leaving any process behind. A process that keeps a build's or a test run's output open does not hold up the
    judge: the engine waits 2 seconds for the output to end after the process it started has exited, then stops reading,
    keeps what was written, and notes the command in the summary and in the evidence (`processes.outputHeld`).

  Each needs deliberate code in the diff, which the person who merges can read. Isolation (the pull request's code in a
  container or a separate job, the evidence computed outside it) is planned after 0.3.0.
- **Not shown:** that the tests were written first. A red commit shows that the tests tell the stub from the
  implementation, which is what the evidence claims.

## The CI judge action

`action/` in this repository is a composite action with three modes, one per job, referenced by commit SHA
(`uses: lockflight/lockflight/action@<40-hex> # vX.Y.Z`), never by a branch or tag that can move:

- **`judge`** (no secret, `contents: read`): installs the engine the target branch pins and the pinned gitleaks, runs
  [`lockflight ci`](commands.md#ci) on the pull request's head commit against the target branch's tip, and uploads
  `lockflight-evidence` (and the gates' `result.json` as `lockflight-result`, under its old name for one release, copied
  from the engine's evidence outside the checkout, so a pull request that commits `artifacts/lockflight/` cannot replace it). Every
  platform value reaches its scripts through `env:`, never interpolated into a script, so a branch name is a string.
- **`review`** (the only job with an API key, for pull requests from the same repository): installs Claude Code with
  `npm ci` from the action's own lockfile (`action/claude-code/package-lock.json`, every package pinned by version and
  integrity hash, so the version moves with the action's pin), runs `lockflight ci review` and uploads `lockflight-review`. The reviewer's reproductions run the pull request's code in this job, so the key can be
  reached by that code: use a spend-limited key scoped to CI.
- **`report`** (checks out the target branch only, `pull-requests: write`): downloads both artifacts as data, runs
  `lockflight ci report` for one status context (`gates` or `lockflight/judge`), writes the job summary, and in one of the
  two report jobs replaces the workflow's own sticky comment (a marker comment anyone else posted is ignored). A failed post
  is a warning, never the verdict.

`gates` keeps its meaning (the `pr` gates and green at head), so a ruleset that requires it keeps working; make
`lockflight/judge` required when the team produces red commits.

The action runs on Linux (x64, Arm64), Windows (x64) and macOS runners: its scripts run in bash (Git Bash on Windows), it
installs the gitleaks build for the runner, and the vendored install script installs `lockflight.exe` on Windows. The
generated workflow runs its jobs on `ubuntu-latest`, and the jobs that build on `windows-latest` when the solution needs
Windows (next section). The engine lock pins the install script by hash, so a Windows runner needs a lock from an engine
release whose `install.sh` knows Windows (released Lockflight 0.3 scripts refuse it); moving the lock to such a release
(`lockflight update`, by a person) vendors that script with it.

### Desktop solutions and Windows runners

A test project **needs Windows** when it targets a `net*-windows` framework (`net8.0-windows`, `net10.0-windows10.0.19041.0`),
or when it, or a `Directory.Build.props`/`.targets` above it, sets `UseWPF` or `UseWindowsForms` to `true`. Its tests run only
on Windows: on Linux the solution does not build (`NETSDK1100`), and with `EnableWindowsTargeting` it builds but the Windows
test hosts cannot start, so a red would look skipped and a server-only fix would fail.

- **`init`** writes the `judge` and `review` jobs (the two that build the pull request's code) with `runs-on: windows-latest`
  and bash as their shell when a test project needs Windows, and says why. The `gates` and `lockflight/judge` report jobs read
  data only and stay on `ubuntu-latest`.
- **`lockflight update --project`** (a person's command) moves the two jobs to `windows-latest` when a test project needs
  Windows, and back to `ubuntu-latest` when none does any more; it prints the diff first, and `--dry-run` shows it without
  writing. It moves only the two labels it writes: a `runs-on` with any other value (a self-hosted label, `windows-2025`, an
  expression) is yours, and is left as it is. To keep Windows for a solution that does not need it, name such a label.
- **`lockflight doctor`** warns when a test project needs Windows and the judge job runs on a runner that is not Windows, and
  when the judge runs on `windows-latest` for a solution that does not need it.
- **The judge off Windows** builds nothing for a head with a test project that needs Windows: every check that builds is ERROR
  with [NEEDS_WINDOWS_RUNNER](reasons.md#needs_windows_runner), never a red or green judged on tests that could not run.

**Cost.** The judge builds the fork point, each red commit and head, and a Windows runner is slower: a judge run on
`windows-latest` takes about twice as long as on Linux. GitHub bills Windows minutes at twice the Linux rate on private
repositories (public repositories run free), so expect about four times the billed minutes of a Linux judge.

### Which check is required

The workflow is named `lockflight` (until 0.3.0 it was `lockflight-gates`; the file keeps its name,
`.github/workflows/lockflight-gates.yml`) and sets two status contexts, `gates` and `lockflight/judge`. Required checks are
matched by those context names, never by the workflow's name, so renaming it breaks no ruleset; `update` renames a 0.3.0
workflow in place.

- **`gates` is the check to require from the start.** It is what Lockflight 0.2's workflow reported, with the same meaning.
- **`lockflight/judge` is advisory until you require it.** When it does not pass while `gates` does, the pull request comment
  marks it "advisory — would block if required" and says what to do: fix what it found (`lockflight ci` gives the same verdict
  locally), or merge knowing it blocks nothing here. Lockflight cannot see your ruleset, so the comment says so for every
  repository.
- **When to require `lockflight/judge`:** once the team produces red commits (`ci.redCommits`, on by default) and the
  routing in `lockflight.yaml` gives your changes the class you mean: `routing.structural` for new projects and build-wide files,
  and `routing.nonProduction` / `routing.generatedPaths` for the parts of the repository outside .NET, so that a change there
  is not judged as a behaviour change ([risk routing](risk-routing.md)). Until then a red judge says something worth reading
  but may block work it should not.

### Moving from Lockflight 0.2's gates workflow

`init` never replaces a workflow that exists, so a project set up with 0.2 keeps a workflow that runs the gates alone, and its
CI says nothing about red/green while a local `lockflight ci` does. `lockflight doctor` warns about it (**workflow: CI
judge**), and `init` run again names the command that moves it. A person moves it in two pull requests, because the CI judge's
workflow runs the engine the target branch pins, and an engine from 0.2 has no CI judge:

```sh
lockflight update --project                                     # first pull request: the new engine pin (and lock); merge it
lockflight update --project --judge-workflow --dry-run          # then: shows the diff of the workflow, writes nothing
lockflight update --project --judge-workflow                    # second pull request: replaces it with the CI judge's workflow
```

`--judge-workflow` refuses while the same update still moves the engine pin. The replacement keeps the file name, the .NET
channels and the trigger (a 0.2 workflow that ran only by hand because of a development build runs on pull requests once a
release is pinned; a project pinned to a development build gets the workflow that runs only by hand, as `init` writes it), and
`gates` keeps its name and meaning. `update` replaces the file only when it is a gates-only workflow as Lockflight wrote it,
apart from the lines Lockflight itself moves (the header, the engine install step between its markers, the policy restore). A
file with other edits is refused and nothing is written: delete it, run `lockflight init` to write the CI judge's workflow, and
add your edits back. A unit repository's workflow, which `workspace sync` writes, runs the gates alone by design and is left to
it. Azure Pipelines and GitLab CI/CD have no judge pipeline yet, so their files are not moved.

## What you add: CODEOWNERS for the protected paths

`policy.protected` makes policy changes visible; CODEOWNERS makes them need a named reviewer. Add to `.github/CODEOWNERS`
(adjust the owner, and add your manifest's own `protected:` globs). It covers every built-in protected path
([configuration reference](configuration.md#built-in-protected-paths)); a pattern without a leading `/` matches at any depth:

```text
# Lockflight policy: changes need a maintainer's review
/lockflight.yaml                         @your-org/maintainers
/.lockflight/                            @your-org/maintainers   # the engine lock and the vendored install script too
/.config/dotnet-tools.json               @your-org/maintainers
/dotnet-tools.json                       @your-org/maintainers
/.claude/settings.json                   @your-org/maintainers
/.claude/settings.local.json             @your-org/maintainers
/.github/workflows/lockflight-gates.yml  @your-org/maintainers
/.github/CODEOWNERS                      @your-org/maintainers
/.gitleaks.toml                          @your-org/maintainers
/.gitleaksignore                         @your-org/maintainers
/global.json                             @your-org/maintainers
Directory.Build.props                    @your-org/maintainers
Directory.Build.targets                  @your-org/maintainers
Directory.Build.rsp                      @your-org/maintainers
Directory.Packages.props                 @your-org/maintainers
NuGet.config                             @your-org/maintainers
nuget.config                             @your-org/maintainers
.editorconfig                            @your-org/maintainers
.globalconfig                            @your-org/maintainers
*.globalconfig                           @your-org/maintainers
*.ruleset                                @your-org/maintainers
stylecop.json                            @your-org/maintainers
BannedSymbols.txt                        @your-org/maintainers
CodeMetricsConfig.txt                    @your-org/maintainers
.gitattributes                           @your-org/maintainers
```

Then make it binding:

1. Open a branch ruleset (or classic branch protection) on `main`.
2. Turn on **Require review from Code Owners**.
3. Turn on **Require status checks: `gates`**.

## Approvals of record: the platform's review

The approval of record is a person approving the pull request on the platform. Lockflight delegates it there: the platform
enforces it, and Lockflight does not read platform reviews. `/lockflight:approve` and `lockflight approve` stay as the local
loop's records (the agent waits for them before it plans or writes code); they are guardrails, not the approval a team relies
on.

**One pull request by default.** The spec, the tasks and the code of a feature go in one pull request, and `specs/**` is owned
by the people who approve specs. Add to `.github/CODEOWNERS`:

```text
# Specs: approving the pull request approves the spec
/specs/                                  @your-org/product-owners
```

With **Require review from Code Owners** on the ruleset (above), a pull request that adds or changes a spec cannot merge
until one of those owners approves it. GitHub never counts the author's own approval; requiring the approval after the most
recent push (**Require approval of the most recent reviewable push**) makes a change after the review need a new one. These
are platform settings, and they are the boundary.

**What the CI judge checks.** The [`approvals` check](commands.md#ci) verifies what git can show: that the base branch's
CODEOWNERS gives every `approvals.specPaths` glob (default `specs/**`) an owner, and, where spec-first applies
(`approvals.default: spec-first`, `approvals.specFirstFrom` for the change's class, or a changed path under
`approvals.specFirstPaths`), that the feature's spec was merged on the base branch first and is unchanged in the code pull
request. Missing coverage, or a spec not merged first, is NOT-EARNED, and the evidence carries a `lockflight.blocked/v1` report
saying who must act; a code pull request that changes an already merged spec under spec-first fails. With
`approvals.check: report` the check is advisory. The spec diff is the first thing in the pull request comment, open up to 200
lines. Lockflight still never reads who approved: the evidence's `platform` state says what it checked (the review is the
platform's), not that someone approved.

**Local records in CI (optional).** To make a pull request fail when a feature it touches has no local approval record, or one
that no longer matches the artifacts, add a step after the gates:

```yaml
      - name: Approvals
        if: github.event_name == 'pull_request'
        env:
          BASE: ${{ github.event.pull_request.base.sha }}
        run: lockflight approve verify --base "$BASE"
```

`approve verify --base` checks every feature the PR touches (changed, renamed or deleted records count as touching it; feature
directories with non-ASCII names are found too). A touched feature whose `specs/` directory no longer exists — deleted, or
renamed away — is reported as `NOTE … removed — nothing to verify`, not a failure; after a rename the new name is verified as
usual, so re-approve it under that name. For every other touched feature, each required approval exists, names its own feature
and phase, and matches the current artifact hashes. A `--base` value starting with `-` is refused. The records are written on
a machine where the agent has your credentials, so this step is a **guardrail**; the review above is the approval of record.
A `.sig` file next to a record (written by a 0.2 engine with `--sign`) is ignored. See
[guardrails and boundaries](guardrails-and-boundaries.md).

## Enable workflows on a new repository

On some new repositories (seen after `gh repo create`), GitHub runs no workflow on `push` or `pull_request` until someone
enables workflows: the Actions tab says "Workflows aren't being run on this repository" and offers "Enable Actions on this
repository", although the Actions permissions API (`gh api repos/{owner}/{repo}/actions/permissions`) answers
`"enabled": true`. No `push` or `pull_request` run starts (a workflow that also has `workflow_dispatch` can still be
started by hand). A ruleset that requires the `gates` check then shows it as
"Expected — Waiting for status to be reported" on every pull request, and nothing will ever report it.

After pushing the workflow `init --ci pr` wrote, open the repository's Actions tab, enable workflows if GitHub asks, then push
to `main` or open a pull request and check that the `lockflight` workflow runs. `init` prints this step when it writes the workflow,
and `lockflight doctor` warns (**github actions**) when the `gh` CLI is signed in and GitHub lists no `push` or `pull_request`
run of the workflow (it skips a fork, whose pull requests run in the upstream repository).

## Plan limits

> [!WARNING]
> On GitHub Free, rulesets and required checks are unavailable for private repositories: the workflow still runs and reports,
> but it is advisory — a red check does not block the merge. Use a public repository, GitHub Pro/Team, or treat the check as a
> manual merge criterion.

## Locally

`gate run --profile pr` works locally too. Without `--base` it scans uncommitted changes and untracked, non-ignored files only
and says so; without gitleaks on
`PATH` (or `LOCKFLIGHT_GITLEAKS`) the secrets step is NOT-EARNED, not PASS. When `LOCKFLIGHT_GITLEAKS_SHA256` is set, the
binary must have that SHA-256 or the secrets step is ERROR: the Azure Pipelines, Bitbucket and GitLab templates set it right
after their gitleaks install, before any of the pull request's code runs, the way the action does. Outside the CI judge the scan runs last, after the
build, as the step order above shows.

## Workspace shells: unit repositories

A workspace shell's CI cannot judge its child repositories: each child has its own pull requests. A unit repository gets the
shell's policy in one of two ways.

### The shell pointer (preferred)

Run `lockflight init --unit-of <shell url> --unit-path <path>` in the unit repository. It writes a `shell:` section into the
unit's own `lockflight.yaml` (the shell's clone URL, optionally its branch, and the unit's path among the shell's `units:`), and
its CI job gets two additions:

1. after the engine install, `lockflight workspace clone --into <temporary dir>` clones the shell: one commit of its branch,
   no checkout. It reads the URL from the unit's `lockflight.yaml`, which on pull requests is restored from the base branch,
   so a pull request cannot point its own CI at another shell;
2. the gates run with `--shell <that dir>` (the CI judge with `lockflight ci --shell <that dir>`; on GitHub the judge's action
   does both when the workflow passes it `shell-clone: "true"` and the token as `shell-token`): the unit's own manifest is made at least as strict as the shell's policy for the
   unit. The shell's protected paths that can match inside the unit are added (re-based as below), the build file and test
   framework of the shell's `units:` entry win over the unit's own (which apply only where the shell names none), and of the
   coverage threshold and the switchable steps (`quality.suppressions`, `docs.references`, `ef.pendingModelChanges`) the
   stricter value wins. The shell's `routing:` is re-based to the unit and merged with the unit's own key by key, the stricter
   value winning ([risk routing in a workspace](risk-routing.md#workspaces)). The run prints the shell commit it read. The
   shell's baselines are not applied yet: the unit's own apply.

The shell's policy is read with `git show` from the clone's commit, never from files a job could change. If the clone fails,
the shell has no valid `lockflight.yaml`, or it does not list the unit, the run is an ERROR, and so is a `--shell` clone whose
`origin` is another repository. A `pr` run in a unit without a shell to read refuses to run rather than judge without the
shell's policy. On a developer's machine, a unit checked out inside its shell finds the shell by itself. The pull request that
adds the pointer is judged by a base without it, as the repository it still is: the clone step then clones nothing.

**Specs from the shell.** The CI judge (`lockflight ci --shell <that dir>`) applies the same policy to the gates it runs, and
reads the unit's spec from the shell: the feature is the one the unit's branch names (`012-checkout`, as Spec Kit names
branches) among the shell's `specs/`, read at the clone's commit, so only a merged spec counts. A unit's pull request answers
for the requirements its red tests name, since one feature's spec can span several units. Until the shell's spec pull request
merges, a unit pull request on a branch named like a feature is NOT-EARNED, with a blocked report naming the spec to merge; run
the check again once it has merged. The approvals check reads the shell's CODEOWNERS (at any platform's location) and specs,
and the evidence records the shell's URL and commit. The change class and which checks are required come from the unit's own
`lockflight.yaml` on its target branch with the shell's `routing:` for the unit merged in, the stricter value winning, as the
gates apply the shell's stricter policy; approvals settings stay the unit's. Locally, a unit checked out
inside its shell is judged against whatever commit the shell has checked out, merged or not.

**Access.** The clone needs read access to the shell repository, and nothing else:

| Shell URL | What to set up |
|---|---|
| `https://` | A read-only token in the `LOCKFLIGHT_SHELL_TOKEN` secret (GitHub: a fine-grained token with read access to the shell's contents; Bitbucket: a repository access token, with `LOCKFLIGHT_SHELL_TOKEN_USER=x-token-auth`; GitLab: a project or deploy token; Azure: a secret variable). It is sent as an HTTP header through git's environment, never on the command line or into the clone. |
| SSH (`git@host:path`) | A read-only deploy key on the shell, loaded by the job's SSH setup (an SSH agent step on GitHub, the repository's SSH key on Bitbucket). |
| Public shell | Nothing. |

In the generated job only the clone step sees the token (GitHub, Azure), or the script unsets it right after the clone, before
any code of the pull request runs (Bitbucket, GitLab). Unsetting is a guardrail: the script's own process still holds the
variable it started with, and code of the pull request running as the same user can read it there (on Linux, from
`/proc/<pid>/environ`). That holds only for the generated file: anyone who can push a branch
to the unit repository can change the job on that branch and read the token, since CODEOWNERS gates the merge, not the run.
Treat the token as readable by every writer of the unit repository, and give it read access to the shell only.

**Boundary.** As for any repository: only where the unit's check is required and CODEOWNERS routes `lockflight.yaml` and the
CI file to a person. The shell's own policy is a boundary for the unit only as far as the shell repository protects it.
`workspace sync` skips a unit whose `lockflight.yaml` points at the shell, and `doctor` in the shell accepts such a manifest.

### Vendored policy (`workspace sync`)

> **Deprecated in 0.3.1, removed in 0.4.** Point each unit at the shell instead
> ([the shell pointer](#the-shell-pointer-preferred)); `lockflight update` in 0.4 moves a vendored unit to the pointer.

`lockflight workspace sync`, run in the shell, vendors into every .NET unit that is its own repository (and has no pointer)
what its CI needs:

| File | What it is |
|---|---|
| `lockflight.unit.yaml` | The shell's policy for the unit, and the shell commit it came from. Every protected path that can match inside the unit is re-based, read as the guard reads it: `services/*/Migrations/**` becomes `Migrations/**`, and a `**` that can reach into the unit is kept, also inside a segment (`services/**.cs` becomes `**.cs`). A path can become more than one glob (`services/**/orders/*.cs` becomes `**/orders/*.cs` and `*.cs`); the result is wider, never narrower, than the shell's rule. It also carries the unit's `build:` file and test framework. |
| `dotnet-tools.json` (or `.config/`) | The engine pin. |
| `.lockflight/baselines/` | The unit's baselines, copied from the shell's `.lockflight/baselines/<unit>/`. |
| `.github/workflows/lockflight-gates.yml` | Generated policy too: an edited copy is drift, and sync rewrites it. |

The workflow restores the policy, the pin, the baselines and the code-style config from the unit's base branch, as the
single-repository workflow does, so a pull request in the unit is judged by its base branch's rules. That holds as a
boundary only where the check is required and the workflow is under code-owner review; the verdict itself is a strong
guardrail, and it blocks a merge only where the platform requires the check.

- **Engine install:** as in the single-repository workflow, the engine is installed with `dotnet tool install --tool-path`
  outside the repository and run directly. A `global.json` that pins SDK 8 cannot restore the engine package.
- **Package sources:** `nuget.config` comes from the base branch, in this workflow and in the single-repository one. A pull
  request that adds a package source cannot make CI install an engine of its own choosing.
- **Policy lookup:** the engine reads `lockflight.unit.yaml` only when no `lockflight.yaml` is found above. Inside the shell,
  the shell's manifest always wins.
- **Drift:** `workspace sync --check` and `doctor` report a unit whose vendored policy is behind the shell's. Commit the synced
  files in each unit after a policy change. The pointer has no drift: CI reads the shell's current policy.
- **Boundary:** it is a boundary only where the unit's `lockflight-gates` check is required, and `lockflight.unit.yaml`, the pin
  and the workflow are routed to a human by CODEOWNERS in the unit repository. The guard also protects them in sessions.

## Next

- [CI on other platforms](ci-other-platforms.md): Azure Pipelines, Bitbucket Pipelines and GitLab CI/CD
- [Guardrails and boundaries](guardrails-and-boundaries.md): which controls are boundaries, and under what conditions
- [Configuration reference](configuration.md#built-in-protected-paths): the built-in protected paths
- [Commands](commands.md#ci): `approve verify` and `gate run` in CI
