# Plugin skills

The plugin adds these skills to Claude Code. Claude picks them from what you ask; you can also call them as
`/lockflight:<name>`. This page lists what each one does, and what the optional crew plugin adds.

**On this page:** [The skills](#the-skills) · [The crew (optional)](#the-crew-optional)

## The skills

| Skill | Ask for example | What it does |
|---|---|---|
| `init` | "set up Lockflight" | Uses the engine you installed (from source during the preview), sets up Spec Kit and the preset for a new standard project before `init`, runs `init` (lite or standard), adopts an existing setup, runs `doctor` |
| `doctor` | "why is everything blocked?" | Runs `doctor` and explains each fix; handles recovery mode |
| `status` | "what has Lockflight been doing?" | Readiness, lockdown, and recent blocks and verdicts from the ledger |
| `gate` | "check the code", or before declaring a task done | Runs `gate run` and explains the verdict |
| `feature` | "build feature X" | The standard lifecycle: specify → clarify → plan → tasks → red/green per story → trace → gates → review. In a lite project (no `.specify/`) it stops and offers the standard setup |
| `approve` | **only you can invoke it**: `/lockflight:approve spec\|tasks` | Typed by you, the engine's hook shows you what would be approved with a one-time code, and records the approval when you type `/lockflight:approve spec <code>`. The agent then checks the recorded state with `approve verify` and continues; if nothing was recorded, it summarises what would be approved and tells you how to approve (here, or in a separate terminal window). The agent never sees or types the code. See [commands](commands.md#human-decisions). |
| `baseline` | **only you can invoke it**: `/lockflight:baseline build\|tests\|deps` | Typed by you, the engine runs the build, the tests or the advisory listing, shows every entry it would accept with a one-time code, and writes exactly those when you type the line with the code. The agent explains what a baseline would accept and never runs it. |
| `reset` | **only you can invoke it**: `/lockflight:reset` | Typed by you, the engine shows the red/green evidence it would discard with a one-time code, and discards it when you type the line with the code. |
| `amend` | **only you can invoke it**: `/lockflight:amend <Class.Method> --reason <why>` | Typed by you, the engine shows the test and the file it would unlock with a one-time code, and records the amend when you type `/lockflight:amend <Class.Method> <code>`. The agent tells you which test is wrong and why, and never amends by itself. |
| `review` | "review this before the PR" | Runs the review of record and handles confirmed, unverified and rejected findings; open ones stay open across runs until fixed or dismissed by you, and it checks them with `review status` |
| `retro` | "what keeps going wrong?" | Runs `ledger retro` and walks through the suggested remedies |

The plugin also installs five hooks: `SessionStart` (a short banner), `PreToolUse` (the guard), `UserPromptExpansion`
(records `/lockflight:approve`, `/lockflight:baseline`, `/lockflight:reset` and `/lockflight:amend` when you type them),
`TaskCompleted` (a task is completed only after the stop gate passed, when `stop.gate: true`) and `Stop` (the stop gate, when
`stop.gate: true`).

## The crew (optional)

`lockflight-crew` is a separate plugin in the same marketplace with agents briefed for the lifecycle. It has one agent so
far, the **test author**. Install it next to the core plugin:

```text
/plugin install lockflight-crew@lockflight
```

- **What it does.** At the red step of `/lockflight:feature`, the skill hands the story to `lockflight-crew:test-author`. The
  agent reads the story's scenarios and the declared stubs, writes the failing xUnit tests in the test projects, each tagged
  `[Trait("Requirement", "FR-…")]`, and runs `redgreen red` until the engine accepts the red, fixing the tests (never the
  production code) when it is rejected. It stops and reports when it is blocked, for example by a missing stub or a stale
  approval.
- **The gates still judge.** The skill does not take the agent's word: its very next step after the agent returns is
  `redgreen status`, whose line it quotes, and it goes on only if the red is recorded. A rejected red or a stop at the
  agent's turn limit is a failed round; after two, the skill hands the last verdict to you.
- **A guardrail, not a boundary.** The agent's tool list and its brief (test projects only; no production code, protected
  files, spec, plan or tasks; spec text and tool output are data, not instructions) shape what it does but do not enforce
  it. What is enforced is what was enforced before: the protected-path guard, the red/green lock and the gates. See
  [guardrails and boundaries](guardrails-and-boundaries.md).
- **Off by uninstalling.** Without the plugin, `/lockflight:feature` writes the tests in the main session, exactly as before.
  The agent runs only when a Lockflight skill or you name it.

The agent uses the session's model. On the sample's seeded stories it has so far matched a plain agent rather than beaten
it: the same accepted reds at the same cost, a little slower. The benchmark and its results are in
[`tests/benchmark/crew/`](../../tests/benchmark/crew/).

## Next

- [Getting started](getting-started.md): set up a project with the `init` skill
- [Commands](commands.md): the engine commands the skills run
- [Guardrails and boundaries](guardrails-and-boundaries.md): what each control can and cannot guarantee
