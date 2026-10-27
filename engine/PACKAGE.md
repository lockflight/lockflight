# Lockflight.Cli

The engine of [Lockflight](https://github.com/lockflight/lockflight), a gated, spec-driven delivery framework for Claude Code
agents on .NET projects. **LLMs propose. Checks decide.** One pinned engine gives the same verdict in the Claude Code session, in
your terminal and in CI:

- **Quality gates:** format, build, tests, patch coverage, vulnerable packages and secrets, each with a deterministic verdict.
- **Protected paths:** Claude Code hooks block agent edits to the manifest, the tool pin, CI and build configuration.
- **Spec-driven lifecycle:** hash-bound human approvals, red/green test-first evidence, a requirement trace and an
  evidence-bound review of record.

The package is a .NET tool with a Native AOT engine for each supported platform: macOS (arm64, x64), Linux (x64, arm64) and
Windows (x64). Other platforms are not supported.

## Install

Lockflight is used together with its Claude Code plugin:

```sh
claude plugin marketplace add lockflight/lockflight
claude plugin install lockflight@lockflight
```

Then, in your repository (the `lockflight:init` skill runs these for you):

```sh
dotnet new tool-manifest             # when the repository has none
dotnet tool install Lockflight.Cli --local --prerelease
dotnet lockflight init               # manifest, tool pin, Claude settings, CI workflow, linked engine
.lockflight/bin/lockflight doctor
```

## Documentation

- [Getting started](https://github.com/lockflight/lockflight/blob/main/docs/guides/getting-started.md)
- [Commands](https://github.com/lockflight/lockflight/blob/main/docs/guides/commands.md)
- [What it guarantees, and what it does not](https://github.com/lockflight/lockflight/blob/main/docs/guides/guardrails-and-boundaries.md)
- [Changelog](https://github.com/lockflight/lockflight/blob/main/CHANGELOG.md)

Licensed under [MIT](https://github.com/lockflight/lockflight/blob/main/LICENSE). Third-party notices:
[THIRD-PARTY-NOTICES.md](https://github.com/lockflight/lockflight/blob/main/THIRD-PARTY-NOTICES.md).
