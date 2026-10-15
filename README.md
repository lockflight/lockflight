<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/banner-dark.svg">
  <img src="docs/assets/banner-light.svg" alt="Lockflight. LLMs propose. Checks decide. Levi the pixel beaver raises a green flag." width="100%">
</picture>

[![CI](https://github.com/lockflight/lockflight/actions/workflows/ci.yml/badge.svg)](https://github.com/lockflight/lockflight/actions/workflows/ci.yml)
![Status: alpha](https://img.shields.io/badge/status-alpha-F2A33A)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![Claude Code plugin](https://img.shields.io/badge/Claude%20Code-plugin-14808F)
[![License: MIT](https://img.shields.io/badge/license-MIT-123A4F)](LICENSE)

[Quick start](#quick-start) · [How it works](#how-a-change-rises) · [Why](#instructions-vs-checks) ·
[What it guarantees](docs/guides/guardrails-and-boundaries.md) · [Docs](docs/README.md)

</div>

**Coding agents write code faster than anyone can review it.** The bottleneck moved from writing code to trusting it. Rules
in `CLAUDE.md`, skills and reviewer agents are instructions, and an agent can talk its way past a paragraph.
**It can't talk its way past an exit code.**

| "Done" isn't evidence | Rules are optional | Tests come last |
|---|---|---|
| The agent says the build is green. Did anything actually run? | "Don't touch the config," says the skill. One `sed` later, it's touched. | Tests written after the code pass by construction and prove little. |

> [!TIP]
> **Lockflight turns your process into checks.** One pinned engine judges every step: in the Claude Code session, in your
> terminal and in CI. Same input, same verdict.

<p align="center">
  <img src="docs/assets/demo-terminal-animated.svg" alt="The guard blocks an agent's edit to lockflight.yaml; the Stop gate keeps a turn with a failing test from ending." width="100%">
</p>

## How a change rises

Like a boat in a canal lock flight, a change climbs stage by stage. Each gate opens only when its check passes, and you
hold the keys to the two that need a person.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/lock-flight-dark.svg">
  <img src="docs/assets/lock-flight-light.svg" alt="Spec and tasks, each approved by you; red, where new tests fail first; green, where they pass with nothing deleted; trace, where every requirement has a test; review, where findings need a failing test; and the pull request, with the same checks in CI." width="100%">
</picture>

## Instructions vs checks

| | Skills, agents and Spec Kit alone | With Lockflight |
|---|---|---|
| **Protected config** | "Please don't edit" in `CLAUDE.md` | The guard blocks the edit, shell writes included |
| **"Tests pass"** | The agent's word | The Stop gate builds and tests before the turn can end |
| **Test-first** | A habit, when remembered | Red/green evidence: new tests fail first, then pass |
| **Spec to code** | A sequence the agent may skip | Hash-bound approvals and phase checks between steps |
| **Coverage of the spec** | Unknown | A trace: every requirement and the tests that prove it |
| **Review** | A reviewer briefed by the coding agent | An independent reviewer; blocking findings need a failing test |
| **CI** | Config a pull request can loosen | The same engine, with the base branch's policy |

## What's in the box

| | |
|---|---|
| **Deterministic gates**<br>Format, build with no new warnings, tests, patch coverage, vulnerable packages, secrets. `PASS`, `FAIL` or `NOT-EARNED`, never "probably". | **Protected paths**<br>The files that define the rules can't be changed by the agent, through edits or the shell ([full list](docs/guides/configuration.md#built-in-protected-paths)). |
| **Spec-driven, on [Spec Kit](https://github.com/github/spec-kit)**<br>A preset, not a fork: requirement IDs, approvals you sign, checks between phases. | **Red/green evidence**<br>New tests must fail before the code exists and pass after, locked while red. xUnit v3 and v2. |
| **Requirement trace**<br>A matrix of every requirement and the tests that went red, then green, for it. | **Evidence-bound review**<br>An independent reviewer whose blocking findings count only with a failing reproduction test. |
| **Adopts your setup**<br>Keeps your hooks, skills, agents and Spec Kit, protects them and flags conflicts. `adopt undo` reverts it. | **One repo or twenty**<br>Workspace shells with service repositories beneath: only changed services are built, and each keeps its own CI policy. |

<table>
<tr>
<td width="140" align="center"><img src="docs/assets/levi-on-duty.svg" alt="Levi, on duty" width="120"></td>
<td><b>Meet Levi.</b> The lock-keeper shows each verdict in your terminal and, if you like, in Claude Code's status line:
a green flag for <code>PASS</code>, a question for <code>NOT-EARNED</code>, a red flag for <code>FAIL</code>, and the barrier
when the guard says no. Only you see him: he stays out of anything the agent reads, so he costs no tokens.</td>
</tr>
</table>

<p align="center">
  <img src="docs/assets/levi-pass.svg" alt="PASS" width="110">&nbsp;&nbsp;
  <img src="docs/assets/levi-not-earned.svg" alt="NOT-EARNED" width="110">&nbsp;&nbsp;
  <img src="docs/assets/levi-fail.svg" alt="FAIL" width="110">&nbsp;&nbsp;
  <img src="docs/assets/levi-blocked.svg" alt="BLOCKED" width="110">
</p>

## Quick start

You need the .NET 10 SDK, git and Claude Code (Git Bash on Windows). Three steps, about five minutes.

**1. Install the engine and the plugin**

```sh
git clone https://github.com/lockflight/lockflight && cd lockflight
scripts/dev-install.sh
```

**2. In Claude Code, at your repository's root**

```
> Set up Lockflight
```

It shows you a plan first: what it keeps, what it protects, what it flags. Nothing changes yet.

**3. Apply it yourself, then commit**

```sh
~/.lockflight/bin/lockflight adopt apply      # the plan gives you the exact command
```

Approvals and adoption are yours to run, in your own terminal: the engine refuses them inside an agent session. Apply backs
up first, and `adopt undo` reverts it. You're gated. The [getting-started guide](docs/guides/getting-started.md)
covers the profiles (`lite` for gates and guard, `standard` for the full lifecycle), workspace shells and the status line.

> [!NOTE]
> **Preview.** The engine installs from source and needs no tokens. A package on nuget.org comes with 1.0.

## Honest about its limits

| Guardrail, on your machine | Boundary, where the agent can't reach |
|---|---|
| The agent runs with your credentials. The guard and the Stop gate prevent accidents and make the right path the easy one. | CI with the base branch's policy, as a required check. Signed approvals can be one too. |

Every control is labelled one or the other: [guardrails and boundaries](docs/guides/guardrails-and-boundaries.md),
[CI hardening](docs/guides/ci-hardening.md).

## Status

**Alpha, in daily use on real projects.** The engine has over 900 unit tests; end-to-end suites cover the lifecycle and
workspace shells on Linux, macOS and Windows, including attempts to get around the controls. A hook adds about 9 ms (p95) on
macOS and 71 ms on Windows.

**Docs:** [getting started](docs/guides/getting-started.md) · [commands](docs/guides/commands.md) ·
[configuration](docs/guides/configuration.md) · [how it works](docs/architecture.md)

## Contributing

Bug reports and ideas are welcome: see [CONTRIBUTING](CONTRIBUTING.md). Before you propose a design change, read
[how it works](docs/architecture.md). C# follows the [code style](docs/engineering/code-style.md).

```sh
cd engine && dotnet build && dotnet test                     # engine
ENGINE=<built lockflight> bash tests/e2e/lifecycle.sh <dir>   # lifecycle end to end
```

## License

[MIT](LICENSE). Built with its own lifecycle; Levi keeps the lock.
