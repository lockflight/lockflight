# How Lockflight works

Lockflight turns a delivery process into checks with exit codes. This page explains the parts and how they fit together, for
anyone who wants to understand the design or change it. For what each command and setting does, see
[commands](guides/commands.md) and [configuration](guides/configuration.md); for what each control can and cannot guarantee,
see [guardrails and boundaries](guides/guardrails-and-boundaries.md).

**On this page:** [Principles](#principles) · [The parts](#the-parts) ·
[The engine on every hook call](#the-engine-on-every-hook-call) · [The guard](#the-guard) · [Gates](#gates) ·
[The lifecycle](#the-lifecycle-standard-profile) · [Topologies](#topologies) · [Adoption](#adoption) ·
[The ledger](#the-ledger) · [Where the trust comes from](#where-the-trust-comes-from)

## Principles

1. **Deterministic where measurable.** If a question can be answered by a command with an exit code, it is. LLM judgment sits
   on top of green commands, never in place of them.
2. **One engine, one policy, one verdict.** The same pinned engine judges in the Claude Code session, in your terminal and in
   CI. The same inputs give the same verdict.
3. **Layers, never one alone.** Routed instructions (`CLAUDE.md`, skills) are a cheap first line; hooks make the engine decide
   on the agent's machine; CI with the base branch's policy decides where the agent can't reach.
4. **Three-valued verdicts.** `PASS`, `FAIL` or `NOT-EARNED`. A check that had nothing to judge (zero tests, a build that
   didn't run) is never a pass.
5. **Humans approve intent, not commands.** People approve the spec and the tasks and merge the pull request; everything in
   between is judged by checks, without per-command prompts.
6. **Honest labels.** Every control is a guardrail (prevents accidents, on your machine) or a boundary (holds against a
   determined agent). Nothing is called enforced unless something outside the agent's reach enforces it.

## The parts

```text
 Claude Code session                 Your terminal                     CI (pull request)
 plugin: skills + hooks              human-only commands               generated workflow
        │                                   │                                 │
        └─────────────── lockflight engine (pinned by the repository) ────────┘
                         gates · guard · lifecycle · trace · review · ledger
                                            │
                       project: lockflight.yaml · .lockflight/ · specs/ · .claude/
```

| Part | What it is | Where |
|---|---|---|
| **Engine** | A C# Native AOT command-line tool, `lockflight`. All decisions are made here. | [`engine/`](../engine/) |
| **Plugin** | Claude Code skills (`/lockflight:*`) and hooks that call the engine. It holds no policy of its own. | [`plugins/lockflight/`](../plugins/lockflight/) |
| **Spec Kit preset** | Templates and commands layered over [Spec Kit](https://github.com/github/spec-kit): requirement IDs, test-first tasks, phase checks. A preset, not a fork. | [`plugins/lockflight/speckit-preset/`](../plugins/lockflight/speckit-preset/) |
| **Project files** | `lockflight.yaml` (the policy), the tool pin, `.lockflight/` (approvals, evidence, baselines, ledger), the CI workflow. | your repository |

## The engine on every hook call

The plugin registers five hooks: `SessionStart`, `PreToolUse`, `UserPromptExpansion`, `TaskCompleted` and `Stop`. Each runs a small shim,
[`lockflight-hook`](../plugins/lockflight/bin/lockflight-hook), which finds the project from `CLAUDE_PROJECT_DIR` and runs the
engine linked into `.lockflight/bin/`. Running the binary directly keeps a hook at a few milliseconds.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/hook-flow-dark.svg">
  <img src="assets/hook-flow-light.svg" alt="A hook call. Claude Code runs the hook shim for SessionStart, PreToolUse, UserPromptExpansion and Stop; the shim finds the project from CLAUDE_PROJECT_DIR and runs the pinned engine in .lockflight/bin, which allows with exit 0 or blocks with exit 2 and the reason on stderr. Without lockflight.yaml the shim does nothing; with the engine missing or stale, or the configuration unreadable, it enters recovery mode." width="100%">
</picture>

The shim has three states:

- **No `lockflight.yaml`:** it does nothing, so the plugin is harmless in other projects.
- **Linked engine of the pinned version:** it runs it, unless the binary is committed to the repository or belongs to
  another user; then it treats the engine as missing and says how to fix it.
- **Engine missing, stale or unreadable configuration:** recovery mode. Reads and the exact restore command are allowed;
  everything else is blocked with the fix.

The contract with Claude Code: a block is **exit 2 with the reason on stderr**; structured answers go to stdout as JSON.
`PreToolUse` is registered with `onFailure: "block"`, so a guard that cannot start blocks rather than allows. Claude Code does
not offer that for `Stop`, which is why the Stop gate is best effort and CI is the definition of done.

`UserPromptExpansion` fires when the person's typed prompt expands a slash command, never for the agent's own skill calls. The
engine ignores every command except `/lockflight:approve`, which it handles only in an interactive, attended session: it shows
the person (on stderr with exit 2, which the agent does not see) what would be approved with a one-time code, and records the
approval when the person types that code while the files are unchanged.

## The guard

`PreToolUse` sends every tool call to the guard. It canonicalises each path it can see (separators, case, drive letters,
symbolic links, Windows short names and alternate streams) and blocks writes to protected paths: `lockflight.yaml`, the tool
pin, `.lockflight/**`, Claude Code settings, the team's `.claude/` hooks, agents, skills and commands, .NET build and analyzer
policy, package sources, the CI job, the paths the manifest adds, and the test projects while a red/green cycle is open (the
[full list](guides/configuration.md#built-in-protected-paths)). Shell commands are parsed for writes (redirections, copies,
in-place editors, archive extractors and more); a write whose target the command line does not show is blocked. Agent
worktrees under `.claude/worktrees/` get the same rules.

`init` also lists the protected paths that exist as write denials for the Claude Code sandbox, so that while the sandbox is
on the operating system refuses a shell command's write to them however the command hides it
([the Claude Code sandbox](guides/guardrails-and-boundaries.md#the-claude-code-sandbox)).

The guard is a guardrail: the agent runs with your credentials. The boundary for the same files is CI, which reads the policy
from the base branch and fails a pull request that changes protected files without a person's review.

## Gates

A gate run executes the steps of a profile in order and ends in one verdict:

| Profile | When | Typical steps |
|---|---|---|
| `edit` | after a change, cheap | format of the changed lines |
| `stop` | before the agent's turn ends | format, build with no new warnings, tests |
| `pr` | in CI, and before a pull request | protected changes, the `stop` steps, patch coverage, vulnerable packages, secrets |

Each step normalises its tool's output instead of trusting the exit code alone (for example `dotnet package list --vulnerable`
exits 0 with findings). Existing problems in a brownfield repository are accepted once, by a person, as baselines, so gates
judge new problems only. Patch coverage judges the changed lines against the base branch, with test code excluded.

The Stop gate runs the `stop` profile when the agent tries to end its turn. It is cached by a hash of its inputs, so an
unchanged tree is not rebuilt, and its block message names the failing tests. In a workspace it builds and tests only the
units that changed since their last PASS: uncommitted changes, a commit, or a shared build input outside every unit.
`lockflight gate run` of the `stop` profile over the stop target caches its PASS the same way.

With the Stop gate on, the `TaskCompleted` hook lets a task be marked completed only when that cache holds a PASS for the tree
as it is now. It reads that evidence and never builds, so it answers within its 30 s timeout. When the Stop gate already sent
the same tree back in this session, it does not block again: the agent is already fixing it, and the Stop gate judges the tree
again before the turn ends. Like the Stop gate it is best effort. Claude Code lets the task complete when the hook fails or
times out, because only exit 2 blocks.

## The lifecycle (standard profile)

```text
spec ──approve──▶ plan + tasks ──approve──▶ red ──▶ green ──▶ trace ──▶ review ──▶ pull request
```

- **Phase checks** (`phase check specify|tasks`) verify structure deterministically: unique requirement IDs, no open
  clarifications, every requirement covered by a task, tests before implementation.
- **Approvals** bind the SHA-256 of the approved files to a person. Any later edit makes the approval stale, and the steps that
  depend on it refuse to run. They can be SSH-signed and verified in CI against signers read from the base branch.
- **Red/green.** The plan declares new signatures; stubs throw `NotImplementedException`. `redgreen red` accepts a cycle only
  when every new test fails because of production code: a stub's `NotImplementedException` thrown from production code
  (`RED_STUB`), or an assertion (xUnit or a recognised assertion or mocking library) in a test that calls production
  code (`RED_ASSERT`). A test that fails to compile, fails in its own code or setup, fails on another exception, fails
  without calling production code, already passes, is skipped or is flaky is rejected. While red, the test projects are locked. `redgreen green` accepts when those tests pass
  and nothing was deleted, skipped or broken elsewhere.
- **Trace** builds the matrix from each requirement (`[Trait("Requirement", "FR-001")]`) to the tests that went red, then
  green, for it. A requirement whose last test is gone fails the trace.
- **Review of record.** `lockflight review` builds the reviewer's prompt itself, from the spec and the diff, and runs an
  independent, read-only Claude call with a fixed output schema. The implementing agent can start a review but cannot brief the
  reviewer. A blocker or major finding counts only when its reproduction test, which the engine compiles and runs, fails on the
  current code. Such a finding stays open across review runs until every reproduction it was given passes or a person dismisses it.

Commands that record a human decision (`approve`, `baseline`, `redgreen reset`, `redgreen amend`, `review dismiss`, `adopt apply|undo`,
`update`) refuse to run inside a Claude Code session.

## Topologies

One model, the **unit**, covers a single repository, a monorepo and a workspace shell (a root repository with its own
`.claude/`, Spec Kit and specs, and service repositories cloned beneath it). `units:` in `lockflight.yaml` lists them. Gates run
per changed unit, each unit keeps its own baselines, and `workspace sync` gives each unit repository the policy and CI workflow
it needs to be judged on its own.

## Adoption

`adopt plan` reads an existing setup (hooks, skills, agents, commands, Spec Kit, `CLAUDE.md`) and proposes an action for each
item: keep, protect, flag, add, upgrade or mark. Nothing changes until a person runs `adopt apply`, which backs up first;
`adopt undo` reverts it. The team's hooks, skills and agents are never removed, and features that predate Lockflight are
marked rather than failed.

## The ledger

The engine appends events (guard blocks, verdicts, rejected reds, confirmed findings, proposed patches) to
`.lockflight/ledger/events-<month>.jsonl`, with credentials masked. `ledger retro` clusters recurring incidents and suggests a
remedy, preferring the most deterministic one; remedies are changes a person reviews. The ledger is local and fail-open: it is
a learning aid, not an audit log.

Each line has a `type`, a `signature` (the fingerprint incidents are grouped by), a `summary` and an optional `detail`; some
types add fields, and readers ignore fields they do not know:

| Type | Extra fields |
|---|---|
| `hook.blocked` | `tool`, `call` (shortened, credentials masked), `session`, `agent` |
| `gate.completed` | `trigger` (`command` for `lockflight gate run`, `stop-hook` for the Stop hook), `durationMs`, `cache` (`hit` when the Stop gate's cache of the last PASS answered, `miss` when it was consulted and the gate ran, `none` when it was not consulted) |
| `approval.recorded` | `feature`, `phase`, `artifacts` (each artifact with the first 12 characters of its hash), `signed`, `channel` (`terminal` for `lockflight approve`, `session` for `/lockflight:approve`) |
| `evidence.recorded`, `config.written` | none |

No line names a person, with one exception: who approved a phase is kept in the approval record, not in the ledger, but
`redgreen:amended` keeps in its detail who amended a test and why, as the red/green record does.

## Where the trust comes from

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/guardrails-boundaries-dark.svg">
  <img src="assets/guardrails-boundaries-light.svg" alt="Guardrails run on your machine, with your credentials: the guard, the Stop gate, red/green evidence and the trace, the review of record, approvals recorded locally. Boundaries sit outside the agent's reach, past the pull request: CI with the base branch's policy as a required check, presence-gated signed approvals verified in CI, and merge when the agent has its own identity." width="100%">
</picture>

| On your machine (guardrails) | Outside the agent's reach (boundaries) |
|---|---|
| The guard, the Stop gate, red/green evidence, trace, the review of record, local approvals | CI running the pinned engine with the base branch's policy as a required check; signed approvals verified in CI; merge by a person, when the agent has its own identity (otherwise it can merge with your token) |

The in-session controls make the right path the easy one and catch accidents early. The pull request is where the verdict
counts.

## Next

- [Guardrails and boundaries](guides/guardrails-and-boundaries.md): what each control can and cannot guarantee
- [CI hardening](guides/ci-hardening.md): make the pull request the place where the verdict counts
- [Getting started](guides/getting-started.md): set it up in a repository
