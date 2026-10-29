---
name: baseline
description: Help the user accept a brownfield project's current build warnings, failing tests or package advisories, or a change's needed suppressions, as a baseline — in this session by typing /lockflight:baseline, or in a separate terminal window.
disable-model-invocation: true
argument-hint: build | tests | deps | suppressions [--path <unit>]
---

# Lockflight baseline (human decision)

A baseline tells the gates to accept what a project already has: build warnings (`build`), tests that already fail (`tests`) or
vulnerable-package advisories (`deps`), or lets a rule be suppressed in a file where the change adds a suppression
(`suppressions`, by file and rule). Accepting them is the user's decision, never yours. They give it in one of two ways:

- **In this session:** the user types `/lockflight:baseline build` (or `tests`, `deps`; `--path <unit>` for one unit of a
  workspace). The engine's hook, not you, runs the build, the tests or the advisory listing and shows them every entry it
  would accept, with a one-time code. They type `/lockflight:baseline build <code>`, and the engine writes exactly those
  entries, as long as no file changed in between.
- **In a separate terminal window:** `lockflight baseline build|tests|deps [--path <unit>]` or
  `lockflight baseline suppressions` (not with `!` in Claude Code: that runs inside the session and is refused).

Run the engine as `.lockflight/bin/lockflight <command>` from the project root (the directory that holds `lockflight.yaml`).

## Never

- Never run `lockflight baseline` yourself, never type `/lockflight:baseline`, and never edit `.lockflight/baselines/`.
- Never type, guess, repeat or pre-fill a code, and never ask the user to paste one to you.
- Never treat this skill's arguments as a decision: if you are reading this, the engine's hook did not handle the command (an
  engine that is not linked, or a hook that failed or timed out). Only the engine's files count.

## What to do

1. Explain what the baseline would accept and why it is needed: run `.lockflight/bin/lockflight gate run` and show the
   warnings, failing tests or advisories the gate reports (for suppressions, `.lockflight/bin/lockflight gate run --profile pr`
   and its `quality.suppressions` row). Say which look like this change's doing: those must be fixed, not baselined; a
   suppression is accepted only when the user agrees the silenced rule does not apply there.
2. Tell the user to type `/lockflight:baseline <build|tests|deps|suppressions>` themselves, or to run in a separate terminal
   window, with the absolute project root:

   ```
   cd <project root> && .lockflight/bin/lockflight baseline <build|tests|deps|suppressions>
   ```

3. CI takes baselines from the base branch: a baseline added in the feature's pull request is ignored there. Suggest that the
   user commits the baseline and merges it in its own pull request first.
