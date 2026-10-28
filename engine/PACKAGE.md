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

Install the engine for your user, outside any project, then the Claude Code plugin:

```sh
cd ~                                   # outside any project: a global.json there could pick an SDK older than 10
dotnet tool install Lockflight.Cli --prerelease --tool-path ~/.lockflight/bin
~/.lockflight/bin/lockflight version
claude plugin marketplace add lockflight/lockflight
claude plugin install lockflight@lockflight --scope user
```

Then, at your repository's root (or ask Claude Code to *"Set up Lockflight"*; the `lockflight:init` skill runs the same
steps):

```sh
~/.lockflight/bin/lockflight init      # manifest, tool pin, Claude settings, CI workflow, linked engine
.lockflight/bin/lockflight doctor      # the linked engine, the way the agent runs every command from here on
```

The engine is installed with `--tool-path`, not as a local tool in the project: the package is built for each platform and
only the .NET 10 SDK can install it, so a local install fails in a repository whose `global.json` pins an older SDK. The
installed engine is a native binary that works whatever SDK the project pins. To update, run
`~/.lockflight/bin/lockflight update` in a project, in a separate terminal window (not with `!` in Claude Code).

## Documentation

- [Getting started](https://github.com/lockflight/lockflight/blob/main/docs/guides/getting-started.md)
- [Commands](https://github.com/lockflight/lockflight/blob/main/docs/guides/commands.md)
- [What it guarantees, and what it does not](https://github.com/lockflight/lockflight/blob/main/docs/guides/guardrails-and-boundaries.md)
- [Changelog](https://github.com/lockflight/lockflight/blob/main/CHANGELOG.md)

Licensed under [MIT](https://github.com/lockflight/lockflight/blob/main/LICENSE). Third-party notices:
[THIRD-PARTY-NOTICES.md](https://github.com/lockflight/lockflight/blob/main/THIRD-PARTY-NOTICES.md).
