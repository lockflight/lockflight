# Contributing

Lockflight is in alpha and maintained by one person. Bug reports, ideas and pull requests are welcome. For anything bigger
than a fix, please open an issue first, because the design still moves.

**On this page:** [Ground rules](#ground-rules) · [Branches, commits and pull requests](#branches-commits-and-pull-requests) ·
[Build and test](#build-and-test) · [Skill evals](#skill-evals) · [Repository map](#repository-map)

## Ground rules

- **Read [how it works](docs/architecture.md) first** for the parts and the principles behind them.
- **Honest claims.** Never describe a control as enforced unless the platform enforces it. See
  [guardrails and boundaries](docs/guides/guardrails-and-boundaries.md).
- **English** for everything committed.
- **C# style:** [`docs/engineering/code-style.md`](docs/engineering/code-style.md). Feature folders, one type per file, `sealed`
  classes and `record` DTOs, injected time, and `CancellationToken` last.

> [!IMPORTANT]
> **Security issues** go through [private reporting](SECURITY.md), not issues.

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
- **Changelog:** pull requests do not edit [CHANGELOG.md](CHANGELOG.md). The release pull request drafts the entries from the
  Conventional Commits on `main` with [git-cliff](https://git-cliff.org) ([cliff.toml](cliff.toml)) and edits them for users, so
  write the pull request title as the line a user should read.
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
claude plugin validate --strict plugins/lockflight-crew                        # optional crew plugin
shellcheck -S warning plugins/lockflight/bin/lockflight-hook tests/*/*.sh       # shell scripts (CI runs this too)
```

### Skill evals

The plugin evals run real Claude Code sessions, so they are not part of CI. Every case has the same shape: `case.yaml`
(name, description, tags, an optional `scaffold.sh` fixture), `prompt.md` (the user's request, turn limit and tools) and
`graders/*.md`. There are trigger cases (a skill loads for its own requests and not for unrelated ones) and boundary cases
(the agent hands a human decision to the person, proposes a protected change instead of writing it). Run them with Bash and
the write tools allowed — several cases only mean something when the agent can try a command and be refused — and with
`--scaffold`, which runs each case's fixture script. The fixtures that need an engine use a small stand-in that gives the
real engine's guard decisions and messages for what the case exercises.

```sh
claude plugin eval plugins/lockflight --runs 3 --ablation none --allow-tools Bash Write Edit --trust-plugin --scaffold --no-publish
```

The crew plugin's evals (`plugins/lockflight-crew/evals/`) dispatch the test author on a small fixture:

```sh
claude plugin eval plugins/lockflight-crew --runs 3 --ablation none --allow-tools Bash Write Edit --trust-plugin --scaffold --no-publish
```

Cases that need both plugins at once (the feature skill dispatching the test author) live in `plugins/evals/`, and run with
`plugins` as the target so both load:

```sh
claude plugin eval plugins --runs 3 --ablation none --allow-tools Bash Write Edit --trust-plugin --scaffold --no-publish
```

[`tests/README.md`](tests/README.md) describes each suite. CI runs them on Linux, Windows and macOS. Before opening a pull
request, run the same steps locally (on Linux use `-r linux-x64`):

```sh
(cd engine && dotnet test) && dotnet publish engine/src/Lockflight.Cli -c Release -r osx-arm64 -o out \
  && ENGINE="$PWD/out/lockflight" sh tests/compat/probe-suite.sh \
  && ENGINE="$PWD/out/lockflight" bash tests/e2e/lifecycle.sh "$(mktemp -d)/lifecycle"
```

## Repository map

| Directory | What it holds |
|---|---|
| [`engine/`](engine/) | the CLI and its unit tests |
| [`plugins/lockflight/`](plugins/lockflight/) | the Claude Code plugin and the Spec Kit preset |
| [`docs/`](docs/) | guides, the architecture overview and the code style |
| [`tests/`](tests/) | cross-component suites |
| [`samples/`](samples/) | fixtures the suites run on |
| [`scripts/`](scripts/) | install from source and asset generation |

## Next

- [How it works](docs/architecture.md): the parts and the principles behind them
- [Code style](docs/engineering/code-style.md): the rules for C#
- [Tests](tests/README.md): what each cross-component suite checks
