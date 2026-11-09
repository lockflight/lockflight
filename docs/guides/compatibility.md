# Compatibility

What Lockflight works with, how much of that each release tests, and how long something that is going away keeps working.
Where a row says "not tested", read it as "expected to work, and nothing in the repository proves it".

**On this page:** [What is public](#what-is-public) · [Deprecations](#deprecations) · [Upgrading a project](#upgrading-a-project) · [.NET SDKs](#net-sdks) ·
[Claude Code](#claude-code) · [Operating systems and CI runners](#operating-systems-and-ci-runners)

## What is public

The formats, names and exit codes that scripts and CI build on are listed in [public contracts](contracts.md), with what
Lockflight promises about each: since 0.3.0 they change only by addition, and the released output of every version is kept and
checked against the current engine. Anything that page does not list (message wording, the ledger, the facts document, files
under `.lockflight/` other than the approval records and the engine lock) is internal and may change in any release.

Versions follow [Semantic Versioning](https://semver.org/). Before 1.0, the minor version plays the role of the major one: 0.4
may remove what 0.3 deprecated, and a patch release removes nothing.

## Deprecations

A public command, option, file or manifest form that is going away is deprecated first:

- **It keeps working, with a warning, for at least one minor release.** The warning names the replacement, and the guides
  mark it deprecated with the release that deprecated it.
- **It is removed no earlier than the next major release** (before 1.0, the next minor). A removed command says in one line
  what replaced it and exits `4`, as a configuration error does.
- **A file format that changes comes with a migration** in `lockflight update --project`, which stays for the whole next major
  line. A format that is a [public contract](contracts.md) changes only by addition; a change of meaning is a new id with that
  migration ([the promise](contracts.md#the-promise)), and what any release since 0.3.0 committed stays readable.

Internal formats ([public contracts](contracts.md#internal-formats)) have no deprecation period.

What has gone this way:

| What | Deprecated | Removed | Replacement |
|---|---|---|---|
| Manifest version 1 (`version: 1`, or no `version`) | 0.3.0: read with migration defaults, named at session start and by `doctor` | 0.4: refused, with the one-line fix | `lockflight update --project` migrates it; the migration stays through 1.x |
| `workspace sync` and the vendored `lockflight.unit.yaml` | 0.3.1: a warning on every run | 0.4: the command says what replaced it | the shell pointer, `init --unit-of`; `lockflight update --project` in a unit repository moves it |
| `doctor --canaries` | renamed to `doctor --self-test` | not yet removed | `doctor --self-test` |

## Upgrading a project

`lockflight update` installs the newest engine, and in a project `lockflight update --project` (which `update` runs for you) brings
what Lockflight generated there to what this release's `init` writes, in one step and with no edit by hand: the engine pin, the
engine lock and the vendored install script, the GitHub workflow (its action pin, steps and jobs, keeping its .NET channels,
trigger, runners and workspace shell clone), the native level's hooks and hook shim, the `CLAUDE.md` block, the `Edit` deny rules
and sandbox write denials, the `.gitignore` entries, and a manifest that needs a migration. `--dry-run` shows the diff and writes
nothing.

- **A workflow you edited is never overwritten.** When the GitHub workflow differs from every template a release wrote,
  `update` leaves it, prints the diff to the current one and writes that to `.lockflight/local/migration/` for you to merge.
  The settings and `.gitignore` are only added to, never pruned. The `CLAUDE.md` block (between its markers) and the hook shim
  are Lockflight's own and are replaced.
- **GitHub only.** The Azure Pipelines, Bitbucket Pipelines and GitLab files are not migrated: `update` moves only the lines it
  always has there (the engine install step, the policy restore and the pull request's base) and says so; compare the rest
  with the templates in [CI on other platforms](ci-other-platforms.md).

## .NET SDKs

| | Supported | Tested |
|---|---|---|
| The engine (building it, or installing it as a `dotnet tool`) | .NET 10 SDK: 10.0.114 or newer in the 1xx band, 10.0.403 or newer in the 4xx band ([contributing](../../CONTRIBUTING.md#build-and-test)). The install script needs no SDK. | every CI run |
| Your projects | SDK-style .NET 8, 9 and 10; .NET Framework is not supported | .NET 10 by the single-repository and lifecycle suites; .NET 8 by the workspace sample's orders service (xUnit v2 on VSTest) and the desktop sample (WPF, with a `global.json` that pins SDK 8). **.NET 9 is not tested:** no suite builds a `net9.0` project. |
| A `global.json` in your repository | the engine runs whatever SDK it pins; a pin to SDK 8 or 9 cannot run `dotnet lockflight` (a .NET 10 tool), so CI and the install script install the engine outside the repository | SDK 8 by the desktop suite; SDK 9 not tested |

The test runners and frameworks each supports are in [getting started](getting-started.md#requirements).

## Claude Code

| | |
|---|---|
| Minimum | 2.1.295: from that version a guard hook that times out blocks the tool call. `doctor` warns on an older one. |
| Tested | 2.1.309: CI validates both plugins and the marketplace with it on every pull request, and the real-hook canary (a headless session that must allow a write and block two protected changes) runs with it on demand. The engine's hook decisions are tested on every run with recorded hook input, without Claude Code. Versions between 2.1.295 and 2.1.309, and later ones, are not tested until the pinned version is raised. |
| Plugin and engine | The plugin and the engine speak hook API 1. The plugin names the oldest engine it works with (`min-engine-version`, 0.3.1 for the 0.3.1 plugin), and a session in a project that pins an older one is told to update it. |

## Operating systems and CI runners

The engine is released for macOS (arm64, x64), Linux (x64, arm64) and Windows (x64). On Windows, Claude Code and the
plugin's hooks run under Git Bash ([install on Windows](getting-started.md#install-on-windows)).

| Platform | Engine and hook suites | End-to-end suites | The CI judge's action |
|---|---|---|---|
| Linux x64 | every pull request | every pull request | every pull request (`ubuntu-latest`) |
| Windows x64 | every pull request | every pull request, Git Bash | every pull request (`windows-latest`) |
| macOS arm64 | every push to `main` | every push to `main` | not supported |
| macOS x64, Linux arm64 | built and released, not tested | not tested | not supported |

The workflow `init` writes runs on `ubuntu-latest`, and moves its building jobs to `windows-latest` when a test project needs
Windows ([desktop solutions](ci-hardening.md#desktop-solutions-and-windows-runners)). GitHub Actions on hosted Linux and
Windows runners is the CI service Lockflight supports; the Azure Pipelines, Bitbucket Pipelines and GitLab templates are not
yet verified on the live services ([CI on other platforms](ci-other-platforms.md)).

## Next

- [Public contracts](contracts.md): every public format and name, and what is promised about each
- [Getting started](getting-started.md): install the engine and the plugin, and set up a project
- [CI on other platforms](ci-other-platforms.md): the state of each CI service
