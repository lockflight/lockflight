# Cross-component tests

Black-box harnesses that run a built engine (and, where noted, the plugin's hook shim) from the outside. Unit tests live next
to the engine in [`engine/tests/`](../engine/tests/) and run with `dotnet test`.

| Suite | What it checks | Runs in |
|---|---|---|
| [`compat/probe-suite.sh`](compat/probe-suite.sh) | LLM-free hook compatibility: Claude Code-shaped hook JSON through `plugins/lockflight/bin/lockflight-hook` into the engine, exit codes for every protected-path, recovery and lockdown case | locally; CI on Linux, macOS, Windows (Git Bash) before a release |
| [`compat/run-windows.ps1`](compat/run-windows.ps1) | The Windows kit: build and unit tests, the compatibility suite, hook latency, and real Claude Code hook forms | manually on a Windows machine |
| [`e2e/lifecycle-xunit2.sh`](e2e/lifecycle-xunit2.sh) | Red/green and trace on xUnit v2 over VSTest (the workspace sample's orders service, .NET 8): metadata listing, RED_STUB, green, trace by Requirement trait, GREEN_BEFORE rejected | locally; CI on Linux, macOS, Windows before a release |
| [`workspace/lifecycle.sh`](workspace/lifecycle.sh) | The lifecycle across a workspace: spec and tasks in the shell with `[unit:…]` tags, red/green and trace in a child repository, the unit's tests locked during red | locally; CI on Linux, macOS, Windows before a release |
| [`workspace/gates.sh`](workspace/gates.sh) | Workspace gates on a materialized [`samples/workspace-shell`](../samples/workspace-shell/) ([`make-shell.sh`](workspace/make-shell.sh)): per-unit protection, only changed units built and tested, VSTest (xUnit v2) and MTP (xUnit v3), the per-unit Stop cache, a broken unit blocking with the test named | locally; CI on Linux, macOS, Windows before a release |
| [`e2e/lifecycle.sh`](e2e/lifecycle.sh) | The standard-profile lifecycle on a copy of [`samples/dotnet-single`](../samples/dotnet-single/): phase checks, approvals, red/green, test lock, trace, and bypass attempts | locally; CI on Linux, macOS, Windows before a release |
| [`canary/run.sh`](canary/run.sh) | A real headless Claude Code session with the plugin: an ordinary write succeeds, a protected file edit and a Bash redirect into a protected path are blocked | scheduled `canary` workflow (needs a token) |

```sh
cd engine && dotnet publish src/Lockflight.Cli -c Release -o ../artifacts/engine && cd ..
ENGINE=$PWD/artifacts/engine/lockflight sh tests/compat/probe-suite.sh
ENGINE=$PWD/artifacts/engine/lockflight bash tests/e2e/lifecycle.sh /tmp/lockflight-lifecycle
```
