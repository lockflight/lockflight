---
name: review
description: Run the review of record for the current feature — an independent, engine-orchestrated reviewer checks the diff against the spec, and blocker/major findings are confirmed by running their reproductions. Use before opening a PR, or when the user asks for a review.
---

# Lockflight review

The engine builds the reviewer's prompt from the spec and the diff; you do not brief the reviewer, because a briefing from the author steers it away from defects. Do not try to
review the change yourself instead — your review of your own work is not independent.

1. Make sure the work is committed or at least saved, and the gates pass (`dotnet lockflight gate run --profile stop`).
2. Run `dotnet lockflight review --base <base branch>` (default `main`; add `--feature <id>` when several features exist).
3. Read the findings:
   - **confirmed** — the reproduction fails on the current code: the defect is real. Fix it in production code, add the
     reproduction as a regression test in the next red/green cycle, and re-run the review.
   - **unverified** — no usable reproduction: show it to the user and let them decide.
   - **not-reproduced** — the reproduction passes: tell the user; it may be dismissed.
   - **reported** (minor/nit findings are not reproduced) — fix when cheap, otherwise list them in the PR body.

   The command exits 1 when a finding is confirmed, 5 (NOT-EARNED) when a blocker/major finding is unverified, and 0 otherwise.
4. Repair cycles are bounded: after two reviews that still confirm findings, stop and ask the user.
5. Mention the review result (verdict, confirmed findings, cost) in the PR body; the evidence file is under `.lockflight/`.

If the command reports NOT-EARNED (no `claude` on PATH, or nothing to review), say so plainly.
