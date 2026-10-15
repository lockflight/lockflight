# Plugin skills

The plugin adds these skills to Claude Code. Claude picks them from what you ask; you can also call them as
`/lockflight:<name>`.

| Skill | Ask for example | What it does |
|---|---|---|
| `init` | "set up Lockflight" | Installs and pins the engine, runs `init` (lite or standard), sets up Spec Kit and the preset for standard, runs `doctor` |
| `doctor` | "why is everything blocked?" | Runs `doctor` and explains each fix; handles recovery mode |
| `status` | "what has Lockflight been doing?" | Readiness, lockdown, and recent blocks and verdicts from the ledger |
| `gate` | "check the code", or before declaring a task done | Runs `gate run` and explains the verdict |
| `feature` | "build feature X" | The standard lifecycle: specify → clarify → plan → tasks → red/green per story → trace → gates |
| `approve` | **only you can invoke it** | Summarises exactly what would be approved and gives you the command to run in your own terminal |
| `review` | "review this before the PR" | Runs the review of record and handles confirmed, unverified and not-reproduced findings |
| `retro` | "what keeps going wrong?" | Runs `ledger retro` and walks through the suggested remedies |

The plugin also installs three hooks: `SessionStart` (a short banner), `PreToolUse` (the guard) and `Stop` (the stop gate,
when `stop.gate: true`).
