# CI hardening

The agent works with your credentials, so CI is where an independent check happens. `lockflight init` writes
`.github/workflows/lockflight-gates.yml`; this page explains what it does and what you add by hand. On Azure Pipelines,
Bitbucket Pipelines or GitLab CI/CD, `init --ci-platform azure|bitbucket|gitlab` writes the same job for that service (not
yet verified on the live services): see [CI on other platforms](ci-other-platforms.md).

**On this page:** [What the generated workflow does](#what-the-generated-workflow-does) ·
[CODEOWNERS for the protected paths](#what-you-add-codeowners-for-the-protected-paths) ·
[Approvals of record](#approvals-of-record-the-platforms-review) · [Plan limits](#plan-limits) · [Locally](#locally) ·
[Workspace shells](#workspace-shells-unit-repositories)

## What the generated workflow does

- **Policy from the base branch.** On pull requests it restores exactly these from the base commit (a file the PR adds is
  removed):
  - `.lockflight/engine.lock` (the engine pin by hash) and `dotnet-tools.json` / `.config/dotnet-tools.json` (its version
    mirror, and the version pin of a project without a lock);
  - `lockflight.yaml`;
  - `.gitleaks.toml` and `.gitleaksignore`;
  - `nuget.config` / `NuGet.config` at the root (package sources: a PR cannot make CI install an engine of its own choosing);
  - `.lockflight/baselines/` (accepted warnings, known-failing tests, advisories) and `.lockflight/ci/` (the vendored engine
    install script);
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
- **Pinned tools.** gitleaks is downloaded at a fixed version and verified against its SHA-256 before use. The engine is the
  one the base branch pins, installed outside the repository (so a `global.json` that pins SDK 8 or 9 does not matter) and run
  as `lockflight`:
  - **With `.lockflight/engine.lock`:** the step checks the vendored `.lockflight/ci/install.sh` against the hash the lock
    names, then runs it: it downloads this platform's package (nuget.org, `LOCKFLIGHT_NUGET_FEED`, or through `dotnet` with
    the root `nuget.config` when that file exists and the variable is unset) and checks the executable against the lock before
    anything runs it. The `dotnet-tools.json` mirror must name the lock's version. The script's hash comes from the lock, not
    from the workflow file, so a pull request that upgrades the engine (script and lock together) is judged by the base's
    script and the base's lock, which agree.
  - **Without a lock** (a project pinned the way Lockflight 0.2 pins, as the base of the pull request that moves it to the
    lock is): the version in the tool pin, with `dotnet tool install --tool-path`. That pull request is judged by the old engine
    and the old policy, and lists the lock, the manifest and the CI files as protected changes for review.
  - The step sits between `# lockflight:install-engine:begin` and `:end`, which `lockflight update` replaces as a whole.
- **Trigger.** On every pull request and push to `main` (`init --ci pr`), or only when started by hand (`--ci manual`). An
  engine pin that is a development build is on no package feed, so `init` writes the manual trigger for it unless you pass
  `--ci pr`; see [CI with a development build](getting-started.md#ci-with-a-development-build).
- **`gate run --profile pr --base <base sha>`**, in this order:
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

A workspace shell's CI cannot judge its child repositories: each child has its own pull requests. A unit repository gets the
shell's policy in one of two ways.

### The shell pointer (preferred)

Run `lockflight init --unit-of <shell url> --unit-path <path>` in the unit repository. It writes a `shell:` section into the
unit's own `lockflight.yaml` (the shell's clone URL, optionally its branch, and the unit's path among the shell's `units:`), and
its CI job gets two additions:

1. after the engine install, `lockflight workspace clone --into <temporary dir>` clones the shell: one commit of its branch,
   no checkout. It reads the URL from the unit's `lockflight.yaml`, which on pull requests is restored from the base branch,
   so a pull request cannot point its own CI at another shell;
2. the gates run with `--shell <that dir>`: the unit's own manifest is made at least as strict as the shell's policy for the
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

**Access.** The clone needs read access to the shell repository, and nothing else:

| Shell URL | What to set up |
|---|---|
| `https://` | A read-only token in the `LOCKFLIGHT_SHELL_TOKEN` secret (GitHub: a fine-grained token with read access to the shell's contents; Bitbucket: a repository access token, with `LOCKFLIGHT_SHELL_TOKEN_USER=x-token-auth`; GitLab: a project or deploy token; Azure: a secret variable). It is sent as an HTTP header through git's environment, never on the command line or into the clone. |
| SSH (`git@host:path`) | A read-only deploy key on the shell, loaded by the job's SSH setup (an SSH agent step on GitHub, the repository's SSH key on Bitbucket). |
| Public shell | Nothing. |

In the generated job only the clone step sees the token (GitHub, Azure), or the script unsets it right after the clone, before
any code of the pull request runs (Bitbucket, GitLab). That holds only for the generated file: anyone who can push a branch
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
single-repository workflow does. A pull request in the unit therefore cannot loosen what judges it.

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
