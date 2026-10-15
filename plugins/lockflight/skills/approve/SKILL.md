---
name: approve
description: Help the user approve a feature's spec or tasks — summarise exactly what would be approved and give the command to run in their own terminal.
disable-model-invocation: true
argument-hint: spec | tasks
---

# Lockflight approve (human gate)

An approval is the user's decision, recorded by the engine against the exact content of the artifacts. The engine refuses to
record it from inside Claude Code, so the user runs the final command in their own terminal.

1. Find the active feature: `dotnet lockflight phase check specify` prints it (or use the feature the user names).
2. Run the matching check — `dotnet lockflight phase check specify` for `spec`, `dotnet lockflight phase check tasks` for
   `tasks` — and show the result. If it fails, say what is missing and stop: approving a failing artifact is pointless.
3. Summarise what is being approved, briefly and faithfully: for `spec` the requirements (FR IDs and one line each) and the user
   stories; for `tasks` the stories, their tests-first order and which requirements each covers. Mention anything that looks
   unclear or risky — the user approves the content, not your summary.
4. Tell the user to run, in their own terminal at the repository root:

   ```
   dotnet lockflight approve <spec|tasks>
   ```

   Add `--sign` to sign the approval with their SSH signing key (a Touch ID / security-key backed key makes it a presence-gated
   approval that CI can verify).
5. Any later edit of an approved file makes the approval stale; the next phase check will ask for a new approval.

Never run `lockflight approve` yourself and never edit files under `.lockflight/`.
