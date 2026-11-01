# CI hardening

The agent works with your credentials, so CI is where an independent check happens. `lockflight init` writes
`.github/workflows/lockflight-gates.yml`; this page explains what it does and what you add by hand. On Bitbucket
Pipelines, `init --ci-platform bitbucket` writes the judge as a pipeline template (not yet run on a live workspace); on GitLab
CI/CD and Azure Pipelines, `init --ci-platform gitlab|azure` writes a job that runs the `pr` gates only (experimental): see
[CI on other platforms](ci-other-platforms.md).

**On this page:** [What the generated workflow does](#what-the-generated-workflow-does) ·
[What the CI judge shows](#what-the-ci-judge-shows-and-what-it-does-not) · [The CI judge action](#the-ci-judge-action) ·
[CODEOWNERS for the protected paths](#what-you-add-codeowners-for-the-protected-paths) ·
[Approvals of record](#approvals-of-record-the-platforms-review) · [Plan limits](#plan-limits) · [Locally](#locally) ·
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
    decode as JSON does), is refused. Without a
    token it only warns. On Azure, Bitbucket and GitLab the first lock is **not** release-checked: the job prints a warning, and
    the person accepting the pull request runs `lockflight engine verify-lock .lockflight/engine.lock` first.
- **Trigger.** On every pull request and push to `main` (`init --ci pr`), or only when started by hand (`--ci manual`). An
  engine pin that is a development build is on no package feed, so `init` writes the manual trigger for it unless you pass
  `--ci pr`; see [CI with a development build](getting-started.md#ci-with-a-development-build).
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

`lockflight ci` re-runs the red/green evidence instead of trusting it. For each commit with a `Lockflight-Red: <cycle>`
trailer it builds that commit and requires its new tests to fail inside production code; it then requires every cycle test to
be the same test at head (or to fail again on the red commit's code when it changed) and to pass there, the spec's
requirements to be traced to those tests, and the `pr` gates to pass, all with the target branch's policy.

- **Read from the target branch** (a boundary, with the judge a required check and a required code-owner review covering
  the workflow files): the policy and baselines the judge applies, the engine the report jobs install, and that the check
  runs at all. Which commits and tests it examined reaches the report jobs through the evidence file, which the next points
  limit.
- **A strong guardrail, not a boundary:** the test results. The tests are the pull request's code and run in the judge job,
  so a determined pull request can forge their outcome. Doing so needs deliberate code in the test assemblies that tampers
  with the test host, which is visible in the test diff; the host-tampering check lists the plain forms of it and is
  NOT-EARNED when one is new.
- **A strong guardrail, not a boundary:** the evidence file itself. In 0.3.0 the judge writes its evidence in the same job
  as the pull request's code, so a process that code leaves running could rewrite it before the report jobs read it.
  Isolation (a container or a separate job for the pull request's code) is planned after 0.3.0.
- **What the pull request's code can affect in the judge job.** In 0.3.0 the pull request's code runs as the same user, in
  the same job, before the judge has finished:
  - leftovers in `obj/` and `bin/` carry from one build to the next in the judge's worktree;
  - git hooks the pull request's code installs can run when the judge checks out paths;
  - the policy, baselines and gitleaks config are read from the worktree after the pull request's code has built and run
    there;
  - test-framework extension points (an xUnit v3 `TestFramework`, an NUnit `IWrapTestMethod` attribute, a custom runner)
    run inside the test host;
  - an MSBuild `<Exec>` task or inline task in a project, props or targets file runs arbitrary code at build time;
  - the host-tampering scan reads the test assemblies only: production code and MSBuild files are not scanned;
  - processes the pull request's code starts are killed only when the job times out.

  Each needs deliberate code in the diff, which the person who merges can read. Isolation (the pull request's code in a
  container or a separate job, the evidence computed outside it) is planned after 0.3.0.
- **Not shown:** that the tests were written first. A red commit shows that the tests tell the stub from the
  implementation, which is what the evidence claims.

## The CI judge action

`action/` in this repository is a composite action with three modes, one per job, referenced by commit SHA
(`uses: lockflight/lockflight/action@<40-hex> # vX.Y.Z`), never by a branch or tag that can move:

- **`judge`** (no secret, `contents: read`): installs the engine the target branch pins and the pinned gitleaks, runs
  [`lockflight ci`](commands.md#ci) on the pull request's head commit against the target branch's tip, and uploads
  `lockflight-evidence` (and the gates' `result.json` as `lockflight-result`, under its old name for one release). Every
  platform value reaches its scripts through `env:`, never interpolated into a script, so a branch name is a string.
- **`review`** (the only job with an API key, for pull requests from the same repository): runs `lockflight ci review` and
  uploads `lockflight-review`. The reviewer's reproductions run the pull request's code in this job, so the key can be
  reached by that code: use a spend-limited key scoped to CI.
- **`report`** (checks out the target branch only, `pull-requests: write`): downloads both artifacts as data, runs
  `lockflight ci report` for one status context (`gates` or `lockflight/judge`), writes the job summary, and in one of the
  two report jobs replaces the workflow's own sticky comment (a marker comment anyone else posted is ignored). A failed post
  is a warning, never the verdict.

`gates` keeps its meaning (the `pr` gates and green at head), so a ruleset that requires it keeps working; make
`lockflight/judge` required when the team produces red commits.

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

## Plan limits

> [!WARNING]
> On GitHub Free, rulesets and required checks are unavailable for private repositories: the workflow still runs and reports,
> but it is advisory — a red check does not block the merge. Use a public repository, GitHub Pro/Team, or treat the check as a
> manual merge criterion.

## Locally

`gate run --profile pr` works locally too. Without `--base` it scans uncommitted changes and untracked, non-ignored files only
and says so; without gitleaks on
`PATH` (or `LOCKFLIGHT_GITLEAKS`) the secrets step is NOT-EARNED, not PASS.

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
