---
name: retro
description: Show Lockflight's retro — recurring incidents from the local ledger (hook blocks, failing gates, rejected reds, confirmed review findings) with a suggested remedy for each. Use when the user asks what keeps going wrong, for a retro, or for lessons learned.
---

# Lockflight retro

1. Run `.lockflight/bin/lockflight ledger retro` from the project root (`--days N` to change the window, `--min N` for the
   recurrence threshold).
2. Present the recurring incidents and the suggested remedy rung for each, most frequent first.
3. For each one the user wants to act on, propose a concrete change at the most deterministic rung that would have prevented
   it: an analyzer or build setting, a check or test, a skill update, a path-scoped rule — and only as a last resort an
   always-on rule. Draft the change as a normal PR for the user to review.
4. Never change gates, hooks, the manifest, protected paths or always-on rules on your own: those always need the user.

The ledger is local and best-effort: treat it as a signal, not as an audit record.
