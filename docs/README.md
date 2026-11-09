# Lockflight documentation

<table>
<tr>
<td width="140" align="center"><img src="assets/levi-on-duty.svg" alt="Levi the pixel beaver, on duty" width="120"></td>
<td>Guides for people who set up and run Lockflight, test-first guardrails for AI coding agents on .NET, and for people who
change Lockflight itself. New here? Start with the <a href="../QUICKSTART.md">quickstart</a> (a first verdict from the
CI judge), then <a href="guides/getting-started.md">getting started</a>, and read
<a href="guides/guardrails-and-boundaries.md">guardrails and boundaries</a> to see what each control can promise.</td>
</tr>
</table>

## Get started

| Page | Read it when |
|---|---|
| [Quickstart](../QUICKSTART.md) | You want a first green verdict from the CI judge on a new solution, with every command, before you set up your own repository. |
| [Getting started](guides/getting-started.md) | You set up your own repository: the engine, the install level, profile and preset, the plugin's lite and standard profiles, and the status line. |
| [Install levels](guides/install-levels.md) | You choose how much runs on developers' machines: CI only, CI + native Claude Code settings (`init --native`), or CI + plugin. |

## Use

| Page | Read it when |
|---|---|
| [Commands](guides/commands.md) | You need every `lockflight` command, its options and its exit codes. |
| [Fix a bug](guides/fix-a-bug.md) | You fix a bug, with an agent or in Visual Studio or Rider, and need the red commit the CI judge asks for, without a spec. |
| [Plugin skills](guides/skills.md) | You want to know what each `/lockflight:*` skill does, and what the optional crew adds. |
| [The spec contract](guides/spec-contract.md) | You write a feature's `spec.md`, `plan.md` and `tasks.md`, by hand or with a tool, and need exactly what the phase checks accept. |
| [Troubleshooting](guides/troubleshooting.md) | Something is blocked or a gate says `NOT-EARNED`: recovery mode, lockdown, Windows, uninstall. |
| [Reasons](guides/reasons.md) | A check or a gate step names a reason code (`GREEN_BEFORE`, `TESTS_FAILED`, …): what it means and what to do next. |

## Configure

| Page | Read it when |
|---|---|
| [Configuration](guides/configuration.md) | You edit `lockflight.yaml`: gate profiles, protected paths, units, baselines, lifecycle commands, environment variables. |
| [CI hardening](guides/ci-hardening.md) | You make CI the check of record: the workflow, which parts are a boundary, CODEOWNERS, required checks, approvals in CI, workspace units. |

## Understand

| Page | Read it when |
|---|---|
| [How it works](architecture.md) | You want the parts and how they fit: the engine, the plugin, the guard, gates, the lifecycle and where the trust comes from. |
| [Risk routing](guides/risk-routing.md) | You want to know how a pull request's change class is computed from its diff: file roles, size, moves, and what each class requires in CI. |
| [Guardrails and boundaries](guides/guardrails-and-boundaries.md) | You need to know what Lockflight can and cannot guarantee, and what the shell write detection sees. |
| [What the judge catches](guides/what-the-judge-catches.md) | You want to know which agent cheats the CI judge blocks, measured against a plain `dotnet test` with a coverage threshold, and which it leaves to review. |
| [Performance](guides/performance.md) | You want to know what the hooks, the Stop gate and the CI judge cost in time on each system, and how to measure them. |
| [Public contracts](guides/contracts.md) | You build on a Lockflight file, output or CI name and need what stays stable across releases, and what is internal. |
| [Compatibility](guides/compatibility.md) | You need which .NET SDKs, Claude Code versions, operating systems and CI runners are supported and tested, or how long a deprecated feature keeps working. |
| [Glossary](guides/glossary.md) | A term is unfamiliar: verdicts, profiles, cycles, approvals, the ledger. |

## Contribute

| Page | Read it when |
|---|---|
| [Contributing](../CONTRIBUTING.md) | You want to report a bug, propose a change or open a pull request. |
| [Code style](engineering/code-style.md) | You write C# for the engine or the packages. |
| [Tests](../tests/README.md) | You run or extend the cross-component suites. |
| [Samples](../samples/README.md) | You need the fixtures the suites run on. |
| [Security policy](../SECURITY.md) | You found a way around a control. |
