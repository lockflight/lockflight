# CI hardening

The agent works with your credentials, so CI is where an independent check happens. `lockflight init` writes
`.github/workflows/lockflight-gates.yml`; this page explains what it does and what you add by hand. On Azure Pipelines,
Bitbucket Pipelines or GitLab CI/CD, `init --ci-platform azure|bitbucket|gitlab` writes the same job for that service (not
yet verified on the live services): see [CI on other platforms](ci-other-platforms.md).

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
  - `dotnet-tools.json` and `.config/dotnet-tools.json` (the engine pin);
  - `lockflight.yaml`;
  - `.gitleaks.toml` and `.gitleaksignore`;
  - `nuget.config` / `NuGet.config` at the root (package sources: a PR cannot make CI install an engine of its own choosing);
  - `.lockflight/baselines/` (accepted warnings, known-failing tests, advisories);
  - code-style and analyzer config anywhere in the tree: `.editorconfig`, `.globalconfig`/`*.globalconfig`, `*.ruleset`,
    `stylecop.json`, `BannedSymbols.txt`, `CodeMetricsConfig.txt`. A PR is judged by the base's style rules, and its own rule changes apply after the
    merge.

  A PR cannot loosen the checks that judge it, pre-accept its own secret or accept its own failing test. On `pull_request`
  events GitHub runs the **PR's** copy of the workflow file itself, so this holds against a hostile PR only together with the
  CODEOWNERS rule and the required check below.

  It does **not** restore what the build itself needs in the PR's version: `Directory.Build.*`, project files,
  nested `NuGet.config` files, `global.json` and `Directory.Packages.props`. A PR can change these, and the gates then build with the
  changed version. For example, a PR could set `<TreatWarningsAsErrors>false</TreatWarningsAsErrors>` in
  `Directory.Build.props`. `policy.protected` lists these files for review. **CODEOWNERS is what protects them.**
- **Pinned tools.** The action is referenced by commit SHA. gitleaks is downloaded at a fixed version and verified against its
  SHA-256 before use. The engine is the version in the target branch's tool pin (read with `git show`), installed with
  `dotnet tool install --tool-path` outside the repository: a `global.json` that pins SDK 8 or 9 cannot restore the engine
  package, so the workflow does not use `dotnet tool restore`.
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

- **Unforgeable from the pull request** (with a required check and CODEOWNERS on the workflow files): which commits and
  tests are examined, the policy, baselines and engine version, and that the check runs at all.
- **A strong guardrail, not a boundary:** the test results. The tests are the pull request's code and run in the judge job,
  so a determined pull request can forge their outcome. Doing so needs deliberate code in the test assemblies that tampers
  with the test host, which is visible in the test diff; the host-tampering check lists the plain forms of it and is
  NOT-EARNED when one is new.
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
/.lockflight/                            @your-org/maintainers
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

**Planned, not built yet:** the CI check will verify that the base branch's CODEOWNERS covers `specs/**`, show the spec diff
first in its pull request comment, and, with `approvals: spec-first` in the manifest, check that the spec was merged on the
base branch first and is unchanged in the code pull request.

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

A workspace shell's CI cannot judge its child repositories: each child has its own pull requests. `lockflight workspace sync`,
run in the shell, vendors into every .NET unit that is its own repository what its CI needs:

| File | What it is |
|---|---|
| `lockflight.unit.yaml` | The shell's policy for the unit, and the shell commit it came from. Every protected path that can match inside the unit is re-based, read as the guard reads it: `services/*/Migrations/**` becomes `Migrations/**`, and a `**` that can reach into the unit is kept, also inside a segment (`services/**.cs` becomes `**.cs`). A path can become more than one glob (`services/**/orders/*.cs` becomes `**/orders/*.cs` and `*.cs`); the result is wider, never narrower, than the shell's rule. It also carries the unit's `build:` file and test framework. |
| `dotnet-tools.json` (or `.config/`) | The engine pin. |
| `.lockflight/baselines/` | The unit's baselines, copied from the shell's `.lockflight/baselines/<unit>/`. |
| `.github/workflows/lockflight-gates.yml` | Generated policy too: an edited copy is drift, and sync rewrites it. |

The workflow restores the policy, the pin, the baselines and the code-style config from the unit's base branch, as the
single-repository workflow does. A pull request in the unit therefore cannot loosen what judges it.

- **Engine install:** as in the single-repository workflow, the engine is installed with `dotnet tool install --tool-path`
  outside the repository and run directly. A `global.json` that pins SDK 8 cannot restore the engine package.
- **Package sources:** `nuget.config` comes from the base branch, in this workflow and in the single-repository one. A pull
  request that adds a package source cannot make CI install an engine of its own choosing.
- **Policy lookup:** the engine reads `lockflight.unit.yaml` only when no `lockflight.yaml` is found above. Inside the shell,
  the shell's manifest always wins.
- **Drift:** `workspace sync --check` and `doctor` report a unit whose vendored policy is behind the shell's. Commit the synced
  files in each unit after a policy change.
- **Boundary:** it is a boundary only where the unit's `lockflight-gates` check is required, and `lockflight.unit.yaml`, the pin
  and the workflow are routed to a human by CODEOWNERS in the unit repository. The guard also protects them in sessions.

## Next

- [CI on other platforms](ci-other-platforms.md): Azure Pipelines, Bitbucket Pipelines and GitLab CI/CD
- [Guardrails and boundaries](guardrails-and-boundaries.md): which controls are boundaries, and under what conditions
- [Configuration reference](configuration.md#built-in-protected-paths): the built-in protected paths
- [Commands](commands.md#ci): `approve verify` and `gate run` in CI
