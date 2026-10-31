<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/banner-dark.svg">
  <img src="docs/assets/banner-light.svg" alt="Lockflight. LLMs propose. Checks decide. Levi the pixel beaver raises a green flag." width="100%">
</picture>

[![CI](https://github.com/lockflight/lockflight/actions/workflows/ci.yml/badge.svg)](https://github.com/lockflight/lockflight/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/vpre/Lockflight.Cli?label=nuget&color=14808F)](https://www.nuget.org/packages/Lockflight.Cli)
![Status: alpha](https://img.shields.io/badge/status-alpha-F2A33A)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![Claude Code plugin](https://img.shields.io/badge/Claude%20Code-plugin-14808F)
[![License: MIT](https://img.shields.io/badge/license-MIT-123A4F)](LICENSE)

[Quickstart](QUICKSTART.md) · [How it works](#how-it-works) · [What the judge checks](#what-the-judge-checks) ·
[What it asks of you](#what-it-asks-of-you) · [Install levels](#install-levels) · [Limits](#honest-about-its-limits) ·
[Docs](docs/README.md) · [Glossary](docs/guides/glossary.md)

</div>

**A CI check for .NET pull requests written by coding agents.** It re-runs the evidence behind "done" (tests that failed
before the code existed and pass after it, unchanged or amended and still failing on the old code) under your base branch's
policy, and reports it in two checks with one verdict each.

**Coding agents write code faster than anyone can review it.** An agent says the build is green and the tests came first.
Instructions in `CLAUDE.md`, skills and reviewer agents ask it to work that way, but an agent can talk its way past a
paragraph. It has a much harder time talking its way past an exit code it did not produce.

| "Done" isn't evidence | Rules are optional | Tests come last |
|---|---|---|
| The agent says the build is green. Did anything actually run? | "Don't touch the config," says the skill. One `sed` later, it's touched. | Tests written after the code pass by construction and prove little. |

> [!TIP]
> **LLMs propose; checks decide.** The judge of record is a CI check: a GitHub Action, and a Bitbucket Pipelines template not
> yet run on a live workspace (GitLab and Azure Pipelines get the gates only, experimental). It builds and runs the pull
> request's commits itself and takes its policy and engine from the target branch. With the check required and a required
> code-owner review on the workflow and the policy, the pull request cannot change the policy that judges it; the results it
> judges are a strong guardrail ([limits](#honest-about-its-limits)). The Claude Code plugin is an optional assistant that
> runs the same checks on the developer's machine, earlier; the crew agents give advice and decide nothing.

## Quick start

> [!NOTE]
> **The CI judge ships in 0.3.0, which is not released yet.** The prerelease on nuget.org (`0.2.0-alpha.9`) has the gates
> workflow but not the judge or `init --ci-only`. To try the judge now, install the engine from source and run it on your
> machine: **[QUICKSTART](QUICKSTART.md)** goes from `git clone` to a first `PASS` on a new solution, every command
> included. We measured 1 min 17 s of machine time.

From 0.3.0 on, the shortest path is the GitHub Action, with nothing installed for Claude Code (the CI-only level). With the
engine installed, at your repository's root:

```sh
~/.lockflight/bin/lockflight init --ci-only      # lockflight.yaml, the engine pin, .github/workflows/lockflight-gates.yml
git add -A && git commit -m "Set up Lockflight"   # on main: the judge reads the policy from the target branch
```

Then, in each pull request, commit the new tests with stubs first, with a trailer
(`git commit --trailer "Lockflight-Red: 1"`), and the implementation after. The workflow sets two checks, `gates` and
`lockflight/judge`, and posts a comment with the verdict. Make `gates` required, and `lockflight/judge` once the team makes
red commits; put the policy and the workflow under CODEOWNERS ([CI hardening](docs/guides/ci-hardening.md)).

## How it works

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/ci-judge-dark.svg">
  <img src="docs/assets/ci-judge-light.svg" alt="The pull request, with its head commit and red commits, goes to three jobs under the target branch's policy, baselines and pinned engine. judge, with no secret, re-runs each red commit, checks green at head with the tests unchanged, the trace and the pr gates. review, optional and with an API key, counts findings only with a failing reproduction. two report jobs run no pull request code: they read both results as data, set the gates and lockflight/judge checks, and one posts the comment. The verdict is PASS, FAIL, NOT-EARNED or ERROR." width="100%">
</picture>

The workflow `init` writes runs the [CI judge action](docs/guides/ci-hardening.md#the-ci-judge-action) in four jobs: `judge`,
`review`, and two report jobs that set `gates` and `lockflight/judge`. Only `review` holds an API key, and only when you turn
it on. The report jobs never run the pull request's code.

## What the judge checks

| Check | What passes |
|---|---|
| **Red** | At each commit with a `Lockflight-Red` trailer, the new tests compile and fail because of production code: a stub that throws (RED_STUB), or an assertion on a result production code returned (RED_ASSERT), not a failure in the test's own setup. The trailer is never trusted: the judge re-runs that commit. |
| **Green** | At head the same tests pass, nothing else fails beyond the accepted baseline, and no test was deleted or skipped. |
| **Tests since red** | Each test that went red is the same test at head (compared by a fingerprint of its compiled code), or an amended one still fails on the red commit's code. |
| **Trace** | With a spec (`specs/<id>/spec.md`, [the spec contract](docs/guides/spec-contract.md)), every requirement has a test that went red and then green. Spec Kit is optional. |
| **Gates** | Format, build with no new warnings, tests, patch coverage, vulnerable packages and secrets; added suppressions and pending EF Core model changes where configured. |
| **Host tampering** | No new code in the test assemblies of the plain kinds that could forge a result: a module initializer, a process exit, a file write outside the temporary folder, setting CI variables, among others. A new finding is NOT-EARNED. It is a static scan: an obfuscated call evades it. |
| **Review** (optional) | With an API key, an independent reviewer's blocking findings count only when a failing reproduction test confirms them. Without a key it is skipped. |
| **Existing tests changed** (advisory) | A changed old test still tells the old behaviour from the new. Reported for the person who merges; it blocks nothing yet. |

Results land in two checks: **`gates`** (the gates and green at head, the meaning existing rulesets rely on) and
**`lockflight/judge`** (routing, red, tests since red, host tampering, trace, review, approvals, existing tests changed).
Approvals check the git facts: CODEOWNERS on the target branch covers the specs and, where spec-first applies, the spec
merged first; the approval itself is the platform's review.

### Four verdicts

<table>
<tr>
<td align="center" width="25%"><img src="docs/assets/levi-pass.svg" alt="Levi raising a green flag" width="88"><br><code>PASS</code></td>
<td align="center" width="25%"><img src="docs/assets/levi-fail.svg" alt="Levi raising a red flag" width="88"><br><code>FAIL</code></td>
<td align="center" width="25%"><img src="docs/assets/levi-not-earned.svg" alt="Levi with a question mark" width="88"><br><code>NOT-EARNED</code></td>
<td align="center" width="25%"><img src="docs/assets/levi-fail.svg" alt="Levi raising a red flag" width="88"><br><code>ERROR</code></td>
</tr>
<tr>
<td>Every required check passed.</td>
<td>A check ran and the code did not pass it: a test that passed before the code existed, a deleted test, a failing gate.</td>
<td>Nothing showed it, so it is never a pass: no red commit, no test for a requirement, a step that could not run.</td>
<td>The engine or a tool it ran broke. Not a verdict about the code.</td>
</tr>
</table>

### Risk routing

How much evidence a pull request needs should follow what it changes, not who opened it. The engine computes a change class
from the diff and the base branch's policy: **fix**, **behaviour**, **feature** (an API or the database) or **critical**
(paths the policy marks critical, the policy itself, the build structure). A class can be raised, never lowered, and a
higher class never needs less. The judge applies the class: red→green when production code changed, a spec from the
feature class, and with the default preset a review with an API key for a critical change, which is NOT-EARNED without one.
`lockflight route show` prints a branch's class before you push. [Risk routing](docs/guides/risk-routing.md) says what each
class requires.

## What it asks of you

- **A red commit in every pull request that changes production code**, humans and hotfixes included: the new tests with a
  stub, committed with a `Lockflight-Red: <n>` trailer before the implementation. At the CI-only level this is by hand
  (`git commit --trailer "Lockflight-Red: 1"`); with the plugin, `redgreen red` makes it. Without one, `lockflight/judge`
  is NOT-EARNED.
- **A spec for a feature-class change** (a new API, a database change) under the default preset: `specs/<id>/spec.md`, by
  hand or with Spec Kit or the crew ([the spec contract](docs/guides/spec-contract.md)).
- **A review with an API key for a critical change** (a policy file, a build-wide file, a new project, an engine upgrade):
  set `ANTHROPIC_API_KEY` and turn the review job on, or such pull requests stay NOT-EARNED (`routing.require.reviewKeyFrom`
  sets the class, or `none`). Each review run spends up to `review.maxUsd`, 3 USD by default.
- **CI time:** the judge builds the fork point, every red commit and the head, and runs their tests, within
  `ci.timeoutMinutes` (45 by default; the job's limit is 10 more).
- **The .NET 10 SDK** where the engine is built from source or installed as a `dotnet tool`, which the plugin level does; the
  install script needs none. Your projects keep their own SDK.
- **A platform plan that can block a merge:** required checks on a private GitHub repository need GitHub Pro, Team or
  Enterprise (a public repository has them); Bitbucket blocks merges on Premium only; on GitLab, code-owner approval needs
  Premium. Elsewhere the check reports, and a person decides.

## Install levels

The CI check is the verdict of record at every level. What changes is how much runs on a developer's machine before the pull
request ([install levels](docs/guides/install-levels.md)).

| | CI only | CI + native settings | CI + plugin |
|---|---|---|---|
| Command | `init --ci-only` | `init --native` | `init` |
| On the machine | nothing | Claude Code deny rules and sandbox settings for the policy files, a `CLAUDE.md` block, hooks that run a linked engine | the plugin: the guard, the Stop gate, skills, `/lockflight:*` commands, the status line |
| For | any agent, or none | Claude Code without a plugin marketplace | Claude Code, with the full local loop |

With the plugin, the guard blocks the agent's common edits to the policy and to tests while they are red, the Stop gate
builds and tests before a turn can end, and `redgreen red` makes the red commits. The optional
[crew](docs/guides/skills.md#the-crew-optional) writes specs and plans to the contract; what it writes is judged like any
other code. Setup: [getting started](docs/guides/getting-started.md).

<p align="center">
  <img src="docs/assets/demo-terminal-animated.svg" alt="With the plugin: the guard blocks an agent's edit to lockflight.yaml; the Stop gate keeps a turn with a failing test from ending." width="100%">
</p>

## Honest about its limits

A **guardrail** prevents accidents but a determined agent or pull request can get around it; a **boundary** holds even
against one, under the conditions named.

| Strong guardrail, not a boundary, in 0.3.0 | Boundary, where the platform enforces it |
|---|---|
| The judge computes its evidence in the same job that runs the pull request's code. Forging a result takes deliberate code in the diff; the host-tampering scan finds the plain forms in test assemblies, and an obfuscated one, production code or an MSBuild task can evade it ([what the pull request's code can affect](docs/guides/ci-hardening.md#what-the-ci-judge-shows-and-what-it-does-not)). Running the pull request's code isolated from the judge is planned after 0.3.0. | The policy, baselines and engine pin read from the target branch; the check itself, as a required check, with the workflow and policy under CODEOWNERS and required review; the spec approved through the platform's review, with CODEOWNERS on `specs/**`. |

- The judge makes faking test-first hard and visible. It does not prove that the agent worked test-first, or that a test
  fully checks its requirement: a person still reads the test diff.
- The review job's API key can be reached by the pull request's code, because the reviewer's reproductions run it. Use a
  spend-limited key scoped to CI.
- Everything on the developer's machine (the guard, local gates, local approvals) is a guardrail: the agent runs with your
  credentials. The guard catches accidents and lets commands it does not parse through.
- The Bitbucket template has not yet run on a live workspace; the GitLab and Azure Pipelines templates are experimental and
  run the gates only.

Every control is labelled in [guardrails and boundaries](docs/guides/guardrails-and-boundaries.md).

## Status

**Alpha, for .NET:** SDK-style .NET 8, 9 and 10 projects. Red/green and trace run on xUnit (v3 and v2), NUnit and MSTest,
in CI and locally; other test projects get the gates' test step. The engine is a Native AOT
binary for macOS, Linux and Windows, with over 3,800 unit tests and end-to-end suites on all three. 0.3.0, the CI judge, is
being finished on `main`; the formats freeze at its release.

**Docs:** [quickstart](QUICKSTART.md) · [getting started](docs/guides/getting-started.md) ·
[CI hardening](docs/guides/ci-hardening.md) · [commands](docs/guides/commands.md) ·
[configuration](docs/guides/configuration.md) · [how it works](docs/architecture.md)

## Contributing

Questions, ideas and stories of how you use Lockflight go to
[Discussions](https://github.com/lockflight/lockflight/discussions); bugs go to
[Issues](https://github.com/lockflight/lockflight/issues/new/choose); a way around a control goes to
[private reporting](SECURITY.md). Pull requests are welcome: see [CONTRIBUTING](CONTRIBUTING.md), and before you propose a
design change, read [how it works](docs/architecture.md). C# follows the [code style](docs/engineering/code-style.md).

```sh
cd engine && dotnet build && dotnet test                     # engine
ENGINE=<built lockflight> bash tests/e2e/lifecycle.sh <dir>   # lifecycle end to end
```

## License

[MIT](LICENSE). Built with its own lifecycle; Levi keeps the lock.
