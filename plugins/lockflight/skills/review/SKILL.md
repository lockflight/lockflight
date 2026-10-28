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
   rebase onto it (ask the user if that is not yours to do) and run it again. If it says the reviewer did not see the whole
   change, the diff is too large for one review: tell the user, and offer to split the change into smaller pull requests,
   each reviewed on its own. Its findings still count, but no run of the whole change can be the review of record.
3. Read the findings. Each has an id, an outcome and, for blocker/major, a state:
   - **confirmed** — the reproduction fails on the current code: the defect is real. Fix it in production code, add the
     reproduction as a regression test in the next red/green cycle, and re-run the review.
   - **contested** — the reproduction fails, but the reviewer's own fix for it breaks one of the change's own tests, or breaks
     only the build of the change's own test files (the output names the tests or files). The reviewer's expectation and the
     author's tests disagree: show the finding and the named tests to the user and let them decide which is right. Change the
     named test only when the user decides the test is the one that is wrong.
   - **unverified** — no usable reproduction: show it to the user and let them decide. A reproduction that did not compile was
     already sent back to the reviewer once with the compiler errors; the evidence file lists them under `compileErrors`.
     `repaired-passed` means the repaired test passed, which does not count as a rejection.
   - **rejected** — the reproduction passes: tell the user.
   - **reported** (minor/nit findings are not reproduced) — fix when cheap, otherwise list them in the PR body.

   A confirmed finding says "fix-checked" when the reviewer's fix made its reproduction pass and kept the change's own tests
   green; that checks the reviewer's evidence, it does not prove the finding. A finding without a fix, or whose fix did not apply or build, is "not fix-checked" and still confirmed.
   A blocker/major finding also says whether the requirement sentence it quotes was found in the spec ("spec quote
   anchored"), was not ("not anchored"), or was not given ("no spec quote"). It does not change the outcome. When a confirmed
   finding's quote is not anchored, tell the user: its test may encode the reviewer's expectation rather than the spec.

   A confirmed, contested or unverified blocker/major finding is **open** and stays open in every later review, also when that review
   does not raise it again ("carried from an earlier run") or raises it again at a lower severity. It closes only when a
   later review re-runs its reproductions — the new one and every earlier one — and all of them pass (**fixed**), or when the
   user dismisses it. Re-running the review is never a way to make a finding go away.
4. Never say a finding is fixed or closed from your own reading: run `.lockflight/bin/lockflight review status` and report what it
   says. If the user thinks a finding is not a defect, ask them to open a separate terminal window (not with `!` in Claude
   Code: that runs inside the session and is refused) and run `.lockflight/bin/lockflight review dismiss <id> --reason "<why>"`
   there themselves. You cannot dismiss findings.
5. The command exits 1 when an open finding is confirmed, 5 (NOT-EARNED) when one is unverified or contested or the run is not of record,
   and 0 otherwise. Repair cycles are bounded: after two reviews that still confirm findings, stop and ask the user.
6. In the PR body, paste the "Pull request summary" from `review status` unchanged: it names the base, the number of review
   runs and every open finding. Add the cost from the last run. The evidence files are under `.lockflight/features/`.

If the command reports NOT-EARNED (no `claude` on PATH, or nothing to review), say so plainly.
