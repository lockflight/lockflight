---
name: status
description: Summarize Lockflight's state for this repository — readiness, lockdown, and recent blocks and gate verdicts from the local ledger. Use when the user asks what Lockflight has been doing, why something was blocked, or for a quick health check.
---

# Lockflight status

1. Run `dotnet lockflight doctor` and summarize it in one line per problem (or "all checks pass").
2. If `.lockflight/lockdown` exists, say the project is in lockdown: only reading is allowed until a human removes the file.
3. Read the newest file in `.lockflight/ledger/` (JSON lines). Summarize the last 20 events: group by `signature`, count
   repeats, and list the most recent `hook.blocked` reasons and `gate.completed` verdicts. A `gate.completed` line's
   `trigger` says who ran it (`command` for `gate run`, `stop-hook` for the Stop gate); `cache: hit` means the Stop gate did
   not run because nothing changed since its last PASS. Repeated signatures are worth pointing out — they are candidates for
   a fix in the next retro.
