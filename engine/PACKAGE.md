# Lockflight.Cli

**Test-first guardrails for AI coding agents on .NET.** Lockflight makes Claude Code prove its work: every change goes
red → green, the build and tests run before an agent can say "done", and a CI judge re-checks that evidence on every pull
request. *LLMs propose. Checks decide.*

This package is the engine of [Lockflight](https://github.com/lockflight/lockflight). One pinned engine gives the same
verdict in the Claude Code session, in your terminal and in CI:

- **Red → green, proven.** New tests must fail on a stub before the implementation exists, then pass unchanged after it. The
  CI judge re-runs both commits itself.
- **"Done" means green.** The Stop gate builds and tests before an agent's turn can end.
- **Rules the agent can't edit.** Claude Code hooks block the agent's edits to the policy, CI and build configuration, and to
  tests while they are red.
- **Quality gates:** format, build with no new warnings, tests, patch coverage, vulnerable packages and secrets.
- **Spec-driven lifecycle:** your approvals, a requirement trace and an evidence-bound review of record.

Native AOT for macOS (arm64, x64), Linux (x64, arm64) and Windows (x64). Projects on .NET 8, 9 and 10; xUnit, NUnit and
MSTest.

## Install

Install the engine for your user, outside any project, then the Claude Code plugin:

```sh
cd ~                                   # outside any project: a global.json there could pick an SDK older than 10
dotnet tool install Lockflight.Cli --tool-path ~/.lockflight/bin
claude plugin marketplace add lockflight/lockflight
claude plugin install lockflight@lockflight --scope user
```

Then open Claude Code in your repository and ask *"Set up Lockflight"*, or run it yourself:

```sh
~/.lockflight/bin/lockflight init             # with the Claude Code plugin
~/.lockflight/bin/lockflight init --ci-only   # only the CI judge, for any agent or none
```

Why `--tool-path` and not a local tool: the package is built for each platform and only the .NET 10 SDK can install it, so a
local install fails in a repository whose `global.json` pins an older SDK. The installed engine is a native binary that works
whatever SDK the project pins. To update, run `~/.lockflight/bin/lockflight update` in a project, in a separate terminal
window.

## Documentation

- [Quickstart: a first PASS in two minutes](https://github.com/lockflight/lockflight/blob/main/QUICKSTART.md)
- [Getting started](https://github.com/lockflight/lockflight/blob/main/docs/guides/getting-started.md)
- [Commands](https://github.com/lockflight/lockflight/blob/main/docs/guides/commands.md)
- [What it guarantees, and what it does not](https://github.com/lockflight/lockflight/blob/main/docs/guides/guardrails-and-boundaries.md)
- [Changelog](https://github.com/lockflight/lockflight/blob/main/CHANGELOG.md)

Licensed under [MIT](https://github.com/lockflight/lockflight/blob/main/LICENSE). Third-party notices:
[THIRD-PARTY-NOTICES.md](https://github.com/lockflight/lockflight/blob/main/THIRD-PARTY-NOTICES.md).
