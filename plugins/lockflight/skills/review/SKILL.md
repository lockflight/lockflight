---
name: review
description: Run the review of record for the current feature — an independent, engine-orchestrated reviewer checks the diff against the spec, and blocker/major findings are confirmed by running their reproductions. Use before opening a PR, or when the user asks for a review.
---

# Lockflight review

The engine builds the reviewer's prompt from the spec and the diff; you do not brief the reviewer, because a briefing from the
author steers it away from defects. Do not try to review the change yourself instead — your review of your own work is not
independent.

1. Make sure the work is committed or at least saved, and the gates pass (`.lockflight/bin/lockflight gate run --profile stop`,
   from the project root: the engine the hooks run).
2. Run `.lockflight/bin/lockflight review` (add `--feature <id>` when several features exist). Leave out `--base`, `--model`
   and `--budget`: the base defaults to the target branch, and only the manifest's settings against the merge base of HEAD and
   the target make the review of record. If the output says the run is not of record because the target moved on, merge or
   rebase onto it (ask the user if that is not yours to do) and run it again.
3. Read the findings. Each has an id, an outcome and, for blocker/major, a state:
   - **confirmed** — the reproduction fails on the current code: the defect is real. Fix it in production code, add the
     reproduction as a regression test in the next red/green cycle, and re-run the review.
   - **unverified** — no usable reproduction: show it to the user and let them decide.
   - **rejected** — the reproduction passes: tell the user.
   - **reported** (minor/nit findings are not reproduced) — fix when cheap, otherwise list them in the PR body.

   A confirmed or unverified blocker/major finding is **open** and stays open in every later review, also when that review
   does not raise it again ("carried from an earlier run"). It closes only when a later review re-runs its reproduction and it
   passes (**fixed**), or when the user dismisses it. Re-running the review is never a way to make a finding go away.
4. Never say a finding is fixed or closed from your own reading: run `.lockflight/bin/lockflight review status` and report what it
   says. If the user thinks a finding is not a defect, ask them to run
   `.lockflight/bin/lockflight review dismiss <id> --reason "<why>"` themselves, in a separate terminal window (not with `!` in
   Claude Code: that runs inside the session and is refused). You cannot dismiss findings.
5. The command exits 1 when an open finding is confirmed, 5 (NOT-EARNED) when one is unverified or the run is not of record,
   and 0 otherwise. Repair cycles are bounded: after two reviews that still confirm findings, stop and ask the user.
6. In the PR body, paste the "Pull request summary" from `review status` unchanged: it names the base, the number of review
   runs and every open finding. Add the cost from the last run. The evidence files are under `.lockflight/features/`.

If the command reports NOT-EARNED (no `claude` on PATH, or nothing to review), say so plainly.
