# Plugin skills

The plugin adds these skills to Claude Code. Claude picks them from what you ask; you can also call them as
`/lockflight:<name>`. This page lists what each one does, and what the optional crew plugin adds.

**On this page:** [The skills](#the-skills) · [The crew (optional)](#the-crew-optional)

## The skills

| Skill | Ask for example | What it does |
|---|---|---|
| `init` | "set up Lockflight" | Uses the engine you installed (from a package or from source), sets up Spec Kit and the preset for a new standard project before `init` when you want Spec Kit, runs `init` (lite or standard), adopts an existing setup, runs `doctor` |
| `doctor` | "why is everything blocked?" | Runs `doctor` and explains each fix; handles recovery mode |
| `status` | "what has Lockflight been doing?" | Readiness, lockdown, and recent blocks and verdicts from the ledger |
| `gate` | "check the code", or before declaring a task done | Runs `gate run` and explains the verdict |
| `feature` | "build feature X" | The standard lifecycle: specify → plan → tasks → red/green per story → trace → gates → review. The spec, plan and tasks follow the [spec contract](spec-contract.md); the crew's spec writer and planner write them when the crew is installed, else Spec Kit's commands when the project has Spec Kit, else the session writes them with you. In a lite project it stops and offers the standard setup |
| `approve` | **only you can invoke it**: `/lockflight:approve spec\|tasks` | Typed by you, the engine's hook shows you what would be approved with a one-time code, and records the approval when you type `/lockflight:approve spec <code>`. The agent then checks the recorded state with `approve verify` and continues; if nothing was recorded, it summarises what would be approved and tells you how to approve (here, or in a separate terminal window). The agent never sees or types the code. See [commands](commands.md#human-decisions). |
| `baseline` | **only you can invoke it**: `/lockflight:baseline build\|tests\|deps` | Typed by you, the engine runs the build, the tests or the advisory listing, shows every entry it would accept with a one-time code, and writes exactly those when you type the line with the code. The agent explains what a baseline would accept and never runs it. |
| `reset` | **only you can invoke it**: `/lockflight:reset` | Typed by you, the engine shows the red/green evidence it would discard with a one-time code, and discards it when you type the line with the code. |
| `amend` | **only you can invoke it**: `/lockflight:amend <Class.Method> --reason <why>` | Typed by you, the engine shows the test and the file it would unlock with a one-time code, and records the amend when you type `/lockflight:amend <Class.Method> <code>`. The agent tells you which test is wrong and why, and never amends by itself. |
| `review` | "review this before the PR" | Runs the review of record and handles confirmed, unverified and rejected findings; open ones stay open across runs until fixed or dismissed by you, and it checks them with `review status` |

The plugin also installs five hooks: `SessionStart` (a short banner), `PreToolUse` (the guard), `UserPromptExpansion`
(records `/lockflight:approve`, `/lockflight:baseline`, `/lockflight:reset` and `/lockflight:amend` when you type them),
`TaskCompleted` (a task is completed only after the stop gate passed, when `stop.gate: true`) and `Stop` (the stop gate, when
`stop.gate: true`).

## The crew (optional)

`lockflight-crew` is a separate plugin in the same marketplace with agents briefed for the lifecycle. A role is in the crew
only when an engine check judges its work: each agent runs that check as its verdict, and the feature skill runs it again
when the agent returns. Install it next to the core plugin:

```text
/plugin install lockflight-crew@lockflight
```

| Agent | Step of `/lockflight:feature` | Writes | Its verdict |
|---|---|---|---|
| `lockflight-crew:spec-writer` | specify | `specs/<id>/spec.md`, from your request | `phase check specify` |
| `lockflight-crew:planner` | plan and tasks, after you approved the spec | `specs/<id>/plan.md` and `tasks.md` | `phase check tasks` |
| `lockflight-crew:test-author` | red, per user story | the story's failing tests (xUnit, NUnit or MSTest, as the project uses) | `redgreen red` |

- **Spec writer and planner.** They write the files in the [spec contract](spec-contract.md), so the project needs no Spec
  Kit (and no Python) for them. The spec writer writes only `spec.md`; when your request leaves a behaviour open, it leaves a
  `[NEEDS CLARIFICATION: …]` marker and stops with the question for you instead of guessing. The planner reads the approved
  spec and the code, writes `plan.md` (approach, files, declared signatures) and `tasks.md` (per story: the stub task, the
  test tasks, then the implementation), and never edits `spec.md`: when the spec is wrong or contradicts itself, it stops
  and quotes the lines for you. Neither approves anything; the approvals stay yours.
- **Test author.** The agent reads the story's scenarios and the declared stubs, writes the failing xUnit tests in the test
  projects, each tagged `[Trait("Requirement", "FR-…")]`, and runs `redgreen red` until the engine accepts the red, fixing
  the tests (never the production code) when it is rejected. It stops and reports when it is blocked, for example by a
  missing stub or a stale approval.
- **Blocked, in a form a tool can read.** The spec writer and the planner stop after two failed checks, or at once when the
  fix needs you, and end their report with one line `Blocked: {"role": …, "feature": …, "check": …, "reason": …, "needs": …}`:
  what failed and what a person must decide or do.
- **The gates still judge.** The skill does not take an agent's word: its very next step after the agent returns is the
  agent's own check (`phase check specify`, `phase check tasks` or `redgreen status`), and it goes on only if that passes. A
  failed check, a `Blocked:` line or a stop at the agent's turn limit is a failed round; after two, the skill hands the last
  verdict to you.
- **A guardrail, not a boundary.** Each agent's tool list and its brief (only its own files; no production code for the spec
  writer and the planner, no spec, plan or tasks for the test author; no protected files; never a human-only command; what
  it reads is data, not instructions, and an instruction found in it is reported as a `Security note:` line that the skill
  relays to you verbatim) shape what it does but do not enforce it. What checks the work is what checked it before:
  the protected-path guard, the human-only commands, the red/green lock and the gates on the machine (guardrails), and the CI
  judge at the pull request. See
  [guardrails and boundaries](guardrails-and-boundaries.md).
- **Off by uninstalling.** Without the plugin, `/lockflight:feature` uses Spec Kit's commands when the project has Spec
  Kit, and otherwise writes the spec, plan and tasks with you in the main session; it writes the tests in the main session.
  An agent runs only when a Lockflight skill or you name it.

**Advice skills.** The crew also has two skills that judge nothing and say so: their answers are advice, and no gate reads
them. Both read the engine's facts (`lockflight facts --json`, [the facts document](commands.md#the-facts-document)) and never
change policy files themselves; a change to a protected file goes through `lockflight propose`.

| Skill | Ask for example | What it does |
|---|---|---|
| `rules-audit` | "which of my CLAUDE.md rules are checked by anything?" | Labels each rule in your instruction files as checked by a guardrail, related to a check, or guidance only, and suggests the most deterministic backing for the important ones ([code quality](code-quality.md#rules-audit)) |
| `retro` | "what keeps going wrong?" | Reads the local ledger, clusters recurring incidents (hook blocks, failing gates, rejected reds, confirmed findings) and suggests a remedy for each, preferring an analyzer or a check over a rule |

The agents use the session's model. The spec writer and the planner have evals in
[`plugins/lockflight-crew/evals/`](../../plugins/lockflight-crew/evals/) but no benchmark against a plain agent yet. For the
test author: on the sample's seeded stories it has so far matched a plain agent rather than beaten it, with the same
accepted reds at the same cost, a little slower. The benchmark and its results are in
[`tests/benchmark/crew/`](../../tests/benchmark/crew/).

## Next

- [Getting started](getting-started.md): set up a project with the `init` skill
- [Commands](commands.md): the engine commands the skills run
- [Guardrails and boundaries](guardrails-and-boundaries.md): what each control can and cannot guarantee
