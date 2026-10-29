# Cross-component tests

Black-box harnesses that run a built engine (and, where noted, the plugin's hook shim) from the outside. This page is for
contributors who run or extend them.

> [!NOTE]
> Unit tests live next to the engine in [`engine/tests/`](../engine/tests/) and run with `dotnet test`.

## Suites

| Suite | What it checks | Runs in |
|---|---|---|
| [`compat/probe-suite.sh`](compat/probe-suite.sh) | LLM-free hook compatibility: Claude Code-shaped hook JSON through `plugins/lockflight/bin/lockflight-hook` into the engine, exit codes for every protected-path, recovery and lockdown case, and an engine replaced after linking (refused for writes; the engine as linked is allowed) | locally; CI on Linux, macOS, Windows (Git Bash) before a release |
| [`compat/run-windows.ps1`](compat/run-windows.ps1) | The Windows kit: build and unit tests, the compatibility suite, hook latency, and real Claude Code hook forms | manually on a Windows machine |
| [`gates/step-order.sh`](gates/step-order.sh) | Which gate steps each profile runs and in what order, with stand-ins for the .NET SDK and gitleaks: a single repository stops at the first step that does not pass; a workspace runs policy and format once at the root, build, test, coverage and dependencies per unit, secrets only when everything before passed, with N/A rows for skipped units; output compared with [`gates/step-order.expected`](gates/step-order.expected) | locally; CI on Linux and macOS |
| [`benchmark/hook-latency.sh`](benchmark/hook-latency.sh) | The PreToolUse hook's latency on five cases (process floor, allowed edit, blocked edit with a ledger write, blocked shell command, agent worktrees); with two engines, both run interleaved with the p50 difference and its 95% confidence interval | manually, before and after a change on the hook path |
| [`e2e/lifecycle-nunit.sh`](e2e/lifecycle-nunit.sh) | Red/green and trace on NUnit ([`samples/dotnet-nunit`](../samples/dotnet-nunit/)), through VSTest and on Microsoft.Testing.Platform: metadata listing, RED_STUB in every `[TestCase]` row, green with an ignored row at the baseline, trace by `[Property("Requirement", …)]`, GREEN_BEFORE, `Assert.Fail` (RED_IN_TEST) and `Assert.Ignore` (SKIPPED) rejected, an NUnit assertion in a parameterized fixture accepted as RED_ASSERT; a `Category!=Integration` stop filter keeps a failing database test out of the regression passes | locally; CI on Linux, macOS, Windows |
| [`e2e/lifecycle-mstest.sh`](e2e/lifecycle-mstest.sh) | Red/green and trace on MSTest ([`samples/dotnet-mstest`](../samples/dotnet-mstest/)), on Microsoft.Testing.Platform and through VSTest: metadata listing, RED_STUB in every `[DataRow]` row, trace by `[TestProperty("Requirement", …)]`, GREEN_BEFORE, `Assert.Fail` (RED_IN_TEST) and `Assert.Inconclusive` (SKIPPED) rejected, an MSTest assertion with `[DynamicData]` rows accepted as RED_ASSERT; a `TestCategory!=Integration` stop filter keeps a failing database test out of the regression passes | locally; CI on Linux, macOS, Windows |
| [`e2e/lifecycle-xunit2.sh`](e2e/lifecycle-xunit2.sh) | Red/green and trace on xUnit v2 over VSTest (the workspace sample's orders service, .NET 8): metadata listing, RED_STUB, green, trace by Requirement trait, GREEN_BEFORE rejected, an assertion-free test flagged | locally; CI on Linux, macOS, Windows before a release |
| [`workspace/lifecycle.sh`](workspace/lifecycle.sh) | The lifecycle across a workspace: spec and tasks in the shell with `[unit:…]` tags, red/green and trace in a child repository, the unit's tests locked during red | locally; CI on Linux, macOS, Windows before a release |
| [`workspace/gates.sh`](workspace/gates.sh) | Workspace gates on a materialized [`samples/workspace-shell`](../samples/workspace-shell/) ([`make-shell.sh`](workspace/make-shell.sh)): per-unit protection, only changed units built and tested (uncommitted, committed, or through a shared build input), VSTest (xUnit v2) and MTP (xUnit v3), the per-unit Stop cache, a broken unit blocking with the test named | locally; CI on Linux, macOS, Windows before a release |
| [`e2e/lifecycle.sh`](e2e/lifecycle.sh) | The standard-profile lifecycle on a copy of [`samples/dotnet-single`](../samples/dotnet-single/): phase checks, approvals, red/green, test lock, trace, and bypass attempts; `test.stopFilter` keeps the Docker test out of the regression passes | locally; CI on Linux, macOS, Windows before a release |
| [`e2e/spec-contract.sh`](e2e/spec-contract.sh) | The complete example in [the spec contract](../docs/guides/spec-contract.md#a-complete-example), extracted from the guide, passes `phase check specify`, then `phase check tasks` once a person approved the spec, in a project without Spec Kit | locally; CI on Linux, macOS, Windows before a release |
| [`benchmark/crew/run.sh`](benchmark/crew/run.sh) | The crew's test author against a plain agent on seeded stories: red accepted and on which attempt, compile reds and other rejections, rounds, wall time, tokens, cost ([README](benchmark/crew/README.md)) | manually; costs real model usage |
| [`Canaries/`](../engine/tests/Lockflight.Cli.Tests/Canaries/) (`dotnet test --filter-trait Category=Canary` in `engine/`) | The engine's canary suite: a planted violation in front of every guard rule, relay guard case, human-only command, in-session command check, gate step, the test gate's judgement, the TaskCompleted hook, and on a generated project the real test gate, red, amend, green and trace, each with a harmless twin; a test fails when a registered guard rule, gate step or human-only command has no canary it catches | CI `canaries` jobs on Linux, macOS, Windows |
| `lockflight doctor --self-test` | The installed engine's quick self-test: five canaries (guard, a human-only command, the linked engine check, the test gate, red) in a scratch project, no build | CI `engine` jobs with the published engine |
| [`install/marketplace-ref.sh`](install/marketplace-ref.sh) | `scripts/marketplace-ref.sh` against a local repository and package index: `set` pins the marketplace to a coherent release tag; `check` refuses a missing tag, a sha that is not the tag's, plugin versions that are not the tag's, a minimum engine above the release, an unpublished engine and a drifted development marketplace | locally; CI on Linux |
| [`release/engine-hashes.sh`](release/engine-hashes.sh) | `scripts/engine-hashes.py` on hand-made packages: the hash is of the executable inside each RID package, `merge` needs exactly the five released platforms and a full commit, `verify` catches a changed executable and a pointer package whose `engine-hashes.json` is missing or different | locally; CI on Linux |
| [`install/install-script.sh`](install/install-script.sh) | `scripts/install.sh` against a local `file://` feed under dash, bash and BusyBox sh (and inside the .NET SDK image, which has only perl to read a zip): install from a lock, a first install by version (hash from the pointer's `engine-hashes.json`), a v3 service index, a hash mismatch (deleted, exit 1), a zip-slip entry, an unknown platform, a development lock, musl, each zip reader alone and none, credentials only on curl's stdin and never in `set -x` output, `--via-dotnet` with a stand-in `dotnet`; the engine is never run | locally; CI on Linux |
| [`install/install-ps1.sh`](install/install-ps1.sh) | A smoke test of `scripts/install.ps1` with PowerShell 7 on a `file://` feed: a lock, a first install by version, a hash mismatch, a usage error | locally (needs `pwsh`); CI on Linux |
| [`canary/run.sh`](canary/run.sh) | A real headless Claude Code session with the plugin: an ordinary write succeeds, a protected file edit and a Bash redirect into a protected path are blocked | scheduled `canary` workflow (needs a token) |

## Run them locally

Publish the engine once, then point each suite at it:

```sh
cd engine && dotnet publish src/Lockflight.Cli -c Release -o ../artifacts/engine && cd ..
ENGINE=$PWD/artifacts/engine/lockflight sh tests/compat/probe-suite.sh
ENGINE=$PWD/artifacts/engine/lockflight bash tests/e2e/lifecycle.sh /tmp/lockflight-lifecycle
```

## Next

- [Contributing](../CONTRIBUTING.md#build-and-test): the full build and test sequence before a pull request
- [Samples](../samples/README.md): the fixtures these suites run on
