# CI hardening

The agent works with your credentials, so CI is where an independent check happens. `lockflight init` writes
`.github/workflows/lockflight-gates.yml`; this page explains what it does and what you add by hand.

**On this page:** [What the generated workflow does](#what-the-generated-workflow-does) ·
[CODEOWNERS for the protected paths](#what-you-add-codeowners-for-the-protected-paths) ·
[Verify approvals in CI](#optional-verify-approvals-in-ci) · [Plan limits](#plan-limits) · [Locally](#locally) ·
[Workspace shells](#workspace-shells-unit-repositories)

## What the generated workflow does

- **Policy from the base branch.** On pull requests it restores exactly these from the base commit (a file the PR adds is
  removed):
  - `dotnet-tools.json` and `.config/dotnet-tools.json` (the engine pin);
  - `lockflight.yaml`;
  - `.gitleaks.toml` and `.gitleaksignore`;
  - `nuget.config` / `NuGet.config` at the root (package sources: a PR cannot make CI install an engine of its own choosing);
  - `.lockflight/baselines/` (accepted warnings, known-failing tests, advisories);
  - code-style and analyzer config anywhere in the tree: `.editorconfig`, `.globalconfig`/`*.globalconfig`, `*.ruleset`,
    `stylecop.json`, `BannedSymbols.txt`. A PR is judged by the base's style rules, and its own rule changes apply after the
    merge.

  A PR cannot loosen the checks that judge it, pre-accept its own secret or accept its own failing test. On `pull_request`
  events GitHub runs the **PR's** copy of the workflow file itself, so this holds against a hostile PR only together with the
  CODEOWNERS rule and the required check below.

  It does **not** restore what the build itself needs in the PR's version: `Directory.Build.*`, project files,
  nested `NuGet.config` files, `global.json` and `Directory.Packages.props`. A PR can change these, and the gates then build with the
  changed version. For example, a PR could set `<TreatWarningsAsErrors>false</TreatWarningsAsErrors>` in
  `Directory.Build.props`. `policy.protected` lists these files for review. **CODEOWNERS is what protects them.**
- **Pinned tools.** gitleaks is downloaded at a fixed version and verified against its SHA-256 before use. The engine is the
  version in the base branch's tool pin, installed with `dotnet tool install --tool-path` outside the repository and run as
  `lockflight`: a `global.json` that pins SDK 8 or 9 cannot restore the engine package, so the workflow does not use
  `dotnet tool restore`.
- **Trigger.** On every pull request and push to `main` (`init --ci pr`), or only when started by hand (`--ci manual`). An
  engine pin that is a development build is on no package feed, so `init` writes the manual trigger for it unless you pass
  `--ci pr`; see [CI with a development build](getting-started.md#ci-with-a-development-build).
- **`gate run --profile pr --base <base sha>`**, in this order:
  1. `policy.protected` — lists protected files the PR changes (never fails; the list goes to the job summary);
  2. `format` → `build` → `test` (with coverage when `gates.coverage.collector` is set);
  3. `coverage.patch` — covered share of the changed coverable lines ≥ `patchThreshold` (default 80 %; N/A below
     `minChangedLines`, default 10, and when `gates.coverage.collector` is not set). A deterministic CI build
     (`ContinuousIntegrationBuild`) may leave `/_/…` source paths in the report; they are read against the repository the
     tests were built in;
  4. `security.deps` — no new High/Critical advisories;
  5. `security.secrets` — gitleaks over the PR's commits, uncommitted changes, and untracked files that git does not ignore.
     False positives go to `.gitleaksignore`, reviewed by a human. An added inline `gitleaks:allow` comment fails the step.

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
.gitattributes                           @your-org/maintainers
```

Then make it binding:

1. Open a branch ruleset (or classic branch protection) on `main`.
2. Turn on **Require review from Code Owners**.
3. Turn on **Require status checks: `gates`**.

## Optional: verify approvals in CI

The generated workflow does not check approvals. To make missing or stale approvals fail a PR, keep an SSH
[allowed signers](https://man.openbsd.org/ssh-keygen#ALLOWED_SIGNERS) file on the base branch (for example
`.github/allowed_signers`, covered by CODEOWNERS) and add a step after the gates:

```yaml
      - name: Approvals
        if: github.event_name == 'pull_request'
        env:
          BASE: ${{ github.event.pull_request.base.sha }}
        run: |
          git show "$BASE:.github/allowed_signers" > "$RUNNER_TEMP/allowed_signers"
          lockflight approve verify --base "$BASE" --allowed-signers "$RUNNER_TEMP/allowed_signers"
```

`approve verify --base` checks every feature the PR touches (changed, renamed or deleted records count as touching it; feature
directories with non-ASCII names are found too). A touched feature whose `specs/` directory no longer exists — deleted, or
renamed away — is reported as `NOTE … removed — nothing to verify`, not a failure; after a rename the new name is verified as
usual, so re-approve it under that name. For every other touched feature, each required approval exists, names its own feature and phase, matches the current artifact
hashes and carries a valid signature from a listed signer. A `--base` value starting with `-` is refused. It is a **boundary** only when the signing key needs your physical
presence (Touch ID or a security key), the signers file comes from the base branch as above, and the job is a required check;
see [guardrails and boundaries](guardrails-and-boundaries.md).

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

- [Guardrails and boundaries](guardrails-and-boundaries.md): which controls are boundaries, and under what conditions
- [Configuration reference](configuration.md#built-in-protected-paths): the built-in protected paths
- [Commands](commands.md#ci): `approve verify` and `gate run` in CI
