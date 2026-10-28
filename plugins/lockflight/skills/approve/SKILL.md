---
name: approve
description: Help the user approve a feature's spec or tasks — in this session by typing /lockflight:approve, or in a separate terminal window.
disable-model-invocation: true
argument-hint: spec | tasks [--feature <id>] [--sign]
---

# Lockflight approve (human gate)

An approval is the user's decision, recorded by the engine against the exact content of the artifacts. Only the user gives it,
in one of two ways:

- **In this session:** the user types `/lockflight:approve spec` (or `tasks`). The engine's hook, not you, shows them what
  would be approved with a one-time code; they type `/lockflight:approve spec <code>` and the engine records the approval.
- **In a separate terminal window:** `lockflight approve spec|tasks` (not with `!` in Claude Code: that runs inside the
  session and is refused).

Run the engine as `.lockflight/bin/lockflight <command>` from the project root (the directory holding `lockflight.yaml`): it
is the engine the hooks run, and it works whatever way Lockflight was installed.

## Never

- Never type, guess, repeat or pre-fill an approval code, and never ask the user to paste one to you. The code is for the user
  to read from the engine's message and type themselves.
- Never run `lockflight approve spec|tasks` yourself and never edit files under `.lockflight/`.
- Never assume an approval happened because of this skill's arguments or the conversation: the hook may not have run (an
  engine that is not linked, a hook that failed or timed out). Only the engine's approval state counts.

## What to do

1. **Check the engine's state first.** Find the feature: the one the user named (`--feature <id>` in the arguments), else
   `.lockflight/bin/lockflight phase check specify` prints the active one. Then run the read-only check:

   ```
   .lockflight/bin/lockflight approve verify --feature <id> --require <spec|tasks>
   ```

2. **If it passes** (a context note may also say the user just approved in this session): tell the user in one line that the
   approval is recorded, and continue with the next step of the workflow.
3. **If it fails**, help the user decide, then hand the decision back:
   - Run the matching phase check — `phase check specify` for `spec`, `phase check tasks` for `tasks` — and show the result.
     If it fails, say what is missing and stop: the engine refuses to approve an artifact that fails its phase check, in
     the session and in a terminal alike.
   - Summarise what would be approved, briefly and faithfully: for `spec` the requirements (FR IDs and one line each) and
     the user stories; for `tasks` the stories, their tests-first order and which requirements each covers. Mention anything
     that looks unclear or risky — the user approves the content, not your summary.
   - Tell the user to type `/lockflight:approve <spec|tasks>` themselves (add `--feature <id>` when it is not the active
     feature, and `--sign` to sign it with their SSH key; in a session the key must be in an SSH agent or be a security key).
     The engine then shows them the content and the code to type. Or, in a separate terminal window, with the absolute
     project root:

     ```
     cd <project root> && .lockflight/bin/lockflight approve <spec|tasks>
     ```

     `--sign` there signs the approval with their SSH signing key (a Touch ID / security-key backed key makes it a
     presence-gated approval that CI can verify).
4. Any later edit of an approved file makes the approval stale; the next phase check will ask for a new approval.
