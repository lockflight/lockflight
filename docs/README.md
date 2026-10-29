# Lockflight documentation

<table>
<tr>
<td width="140" align="center"><img src="assets/levi-on-duty.svg" alt="Levi the pixel beaver, on duty" width="120"></td>
<td>Guides for people who set up and run Lockflight around a Claude Code agent, and for people who change Lockflight itself.
New here? Start with <a href="guides/getting-started.md">getting started</a>, then read
<a href="guides/guardrails-and-boundaries.md">guardrails and boundaries</a> to see what each control can promise.</td>
</tr>
</table>

## Get started

| Page | Read it when |
|---|---|
| [Getting started](guides/getting-started.md) | You are installing Lockflight and setting up a repository: the lite and standard profiles, step by step, and the status line. |

## Use

| Page | Read it when |
|---|---|
| [Commands](guides/commands.md) | You need every `lockflight` command, its options and its exit codes. |
| [Plugin skills](guides/skills.md) | You want to know what each `/lockflight:*` skill does, and what the optional crew adds. |
| [The spec contract](guides/spec-contract.md) | You write a feature's `spec.md`, `plan.md` and `tasks.md`, by hand or with a tool, and need exactly what the phase checks accept. |
| [Troubleshooting](guides/troubleshooting.md) | Something is blocked or a gate says `NOT-EARNED`: recovery mode, lockdown, Windows, uninstall. |

## Configure

| Page | Read it when |
|---|---|
| [Configuration](guides/configuration.md) | You edit `lockflight.yaml`: gate profiles, protected paths, units, baselines, lifecycle commands, environment variables. |
| [CI hardening](guides/ci-hardening.md) | You make CI the boundary: the generated workflow, CODEOWNERS, required checks, approvals in CI, workspace units. |

## Understand

| Page | Read it when |
|---|---|
| [How it works](architecture.md) | You want the parts and how they fit: the engine, the plugin, the guard, gates, the lifecycle and where the trust comes from. |
| [Guardrails and boundaries](guides/guardrails-and-boundaries.md) | You need to know what Lockflight can and cannot guarantee, and what the shell write detection sees. |
| [Glossary](guides/glossary.md) | A term is unfamiliar: verdicts, profiles, cycles, approvals, the ledger. |

## Contribute

| Page | Read it when |
|---|---|
| [Contributing](../CONTRIBUTING.md) | You want to report a bug, propose a change or open a pull request. |
| [Code style](engineering/code-style.md) | You write C# for the engine or the packages. |
| [Tests](../tests/README.md) | You run or extend the cross-component suites. |
| [Samples](../samples/README.md) | You need the fixtures the suites run on. |
| [Security policy](../SECURITY.md) | You found a way around a control. |
