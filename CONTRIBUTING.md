# Contributing

Lockflight is in alpha and maintained by one person. Bug reports, ideas and pull requests are welcome. For anything bigger
than a fix, please open an issue first, because the design still moves.

## Ground rules

- **Read [how it works](docs/architecture.md) first** for the parts and the principles behind them.
- **Honest claims.** Never describe a control as enforced unless the platform enforces it. See
  [guardrails and boundaries](docs/guides/guardrails-and-boundaries.md).
- **English** for everything committed.
- **C# style:** [`docs/engineering/code-style.md`](docs/engineering/code-style.md). Feature folders, one type per file, `sealed`
  classes and `record` DTOs, injected time, and `CancellationToken` last.
- **Security issues** go through [private reporting](SECURITY.md), not issues.

## Branches, commits and pull requests

- **Branches:** `<type>/<area>-<what>`, for example `fix/guard-tee-append`, `feat/gates-api-compat`,
  `docs/getting-started-windows`.
- **Commits** follow [Conventional Commits](https://www.conventionalcommits.org/): `<type>(<scope>): <summary>` in the
  imperative, at most 72 characters, with a body that says why.
  - Types: `feat`, `fix`, `perf`, `refactor`, `docs`, `test`, `build`, `ci`, `chore`. A breaking change adds `!`
    (`feat(cli)!: …`) and a `BREAKING CHANGE:` footer.
  - Scopes: `guard`, `gates`, `redgreen`, `trace`, `review`, `approve`, `lifecycle`, `adopt`, `init`, `doctor`, `workspace`,
    `ledger`, `cli`, `plugin`, `speckit`, `ci`, `deps`. Leave the scope out when a change spans several.
- **Pull requests** are squash-merged, so the title becomes the commit on `main` and follows the same format. Fill in the
  [template](.github/pull_request_template.md): what and why, how it was verified, the effect on guarantees, breaking changes.
  One concern per pull request.
- **Changelog:** add a line under `Unreleased` in [CHANGELOG.md](CHANGELOG.md) for anything a user notices.
  [git-cliff](https://git-cliff.org) drafts release notes from the commits ([cliff.toml](cliff.toml)).
- **Versions** follow [Semantic Versioning](https://semver.org/); releases are tagged `vX.Y.Z[-alpha.N]`. The plugin and the
  engine share one version. The plugin's version in `plugin.json` is the last release: `scripts/dev-install.sh` numbers
  development builds after it (`0.2.0-alpha.4.dev.<commit time>`, or `0.2.1-0.dev.<commit time>` after a release `0.2.0`) so
  they sort between that release and the next. `plugins/lockflight/min-engine-version` names the oldest released engine the
  plugin works with; raise it in the change that makes the plugin depend on something newer.

## Build and test

Requirements: .NET SDK 10 (see [`engine/global.json`](engine/global.json)). For Native AOT publishing, the platform's C++
toolchain is also needed.

```sh
cd engine
dotnet build
dotnet test                      # unit tests (do not pass --nologo: it is forwarded to the test host)
dotnet publish src/Lockflight.Cli -c Release -o ../artifacts/engine
cd ..
ENGINE=$PWD/artifacts/engine/lockflight sh tests/compat/probe-suite.sh          # hook compatibility
ENGINE=$PWD/artifacts/engine/lockflight bash tests/e2e/lifecycle.sh /tmp/lf-e2e # lifecycle end to end
claude plugin validate --strict plugins/lockflight                              # plugin manifest
shellcheck -S warning plugins/lockflight/bin/lockflight-hook tests/*/*.sh       # shell scripts (CI runs this too)
```

### Skill evals

The plugin's evals (`plugins/lockflight/evals/`) run real Claude Code sessions, so they are not part of CI. Run them with Bash
allowed — several cases only mean something when the agent can try a command and be refused (the no-self-approval case, for
example, checks that the agent asks the user instead of approving):

```sh
claude plugin eval plugins/lockflight --runs 3 --ablation none --allow-tools Bash --trust-plugin --no-publish
```

[`tests/README.md`](tests/README.md) describes each suite. CI runs them on Linux, Windows and macOS. Before opening a pull
request, run the same steps locally (on Linux use `-r linux-x64`):

```sh
(cd engine && dotnet test) && dotnet publish engine/src/Lockflight.Cli -c Release -r osx-arm64 -o out \
  && ENGINE="$PWD/out/lockflight" sh tests/compat/probe-suite.sh \
  && ENGINE="$PWD/out/lockflight" bash tests/e2e/lifecycle.sh "$(mktemp -d)/lifecycle"
```

## Repository map

- [`engine/`](engine/) — the CLI and its unit tests
- [`plugins/lockflight/`](plugins/lockflight/) — the Claude Code plugin and the Spec Kit preset
- [`docs/`](docs/) — guides, the architecture overview and the code style
- [`tests/`](tests/) — cross-component suites
- [`samples/`](samples/) — fixtures the suites run on
- [`scripts/`](scripts/) — install from source and asset generation
