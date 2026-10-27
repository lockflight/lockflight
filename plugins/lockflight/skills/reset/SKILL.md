---
name: reset
description: Help the user discard a feature's red/green test-first evidence — in this session by typing /lockflight:reset, or with lockflight redgreen reset in a separate terminal window.
disable-model-invocation: true
argument-hint: "[--feature <id>]"
---

# Lockflight reset (human decision)

`redgreen reset` deletes a feature's red/green evidence: the baseline of its tests and every cycle. The test projects unlock,
and red/green starts again with `redgreen start`. Only the user decides that. They do it in one of two ways:

- **In this session:** the user types `/lockflight:reset` (with `--feature <id>` when it is not the active feature). The
  engine's hook, not you, shows them the evidence that would be discarded, with a one-time code. They type
  `/lockflight:reset <code>`, and the engine discards it, as long as the evidence did not change in between.
- **In a separate terminal window:** `lockflight redgreen reset [--feature <id>]` (not with `!` in Claude Code: that runs
  inside the session and is refused).

## Never

- Never run `lockflight redgreen reset` yourself, never type `/lockflight:reset`, and never edit or delete files under
  `.lockflight/`.
- Never type, guess, repeat or pre-fill a code, and never ask the user to paste one to you.
- Never assume a reset happened: if you are reading this, the engine's hook did not handle the command. Check with
  `.lockflight/bin/lockflight redgreen status`.

## What to do

1. Run `.lockflight/bin/lockflight redgreen status` and show the user the cycles a reset would discard.
2. If only one test of the open cycle has to change, a reset is more than needed: suggest `/lockflight:amend` instead (see
   the `amend` skill), which keeps the rest of the evidence.
3. Otherwise tell the user to type `/lockflight:reset` themselves, or to run in a separate terminal window, with the absolute
   project root:

   ```
   cd <project root> && .lockflight/bin/lockflight redgreen reset
   ```

4. After the reset, check `redgreen status` and start again from `redgreen start`.
