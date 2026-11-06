---
name: check-pr
description: Run Lockflight's judge locally before a pull request is opened or pushed — `lockflight ci --platform local` on the committed branch, the same checks the CI judge runs with the target branch's policy — read its verdict and explain each check that did not pass. Use when the user asks whether the branch is ready for a PR, asks to open, create or push a pull request, or asks what the CI judge will say.
---

# Lockflight check PR

The CI judge on the pull request is the verdict of record. The local judge runs the same checks with the same engine and the
target branch's policy, so it says before the push what CI will say, and a failure costs a local run instead of a CI round.

Run the engine as `.lockflight/bin/lockflight <command>` from the project root (the directory holding `lockflight.yaml`).

1. **When.** Before opening a pull request, and before pushing new commits to one. Run the gates first
   (`/lockflight:gate`); the judge builds every red commit, so it takes longer.
2. **Commit.** The judge checks commits: commit the work (or ask the user to stash what is not part of it). It refuses an
   uncommitted working tree.
3. **Run** `.lockflight/bin/lockflight ci --platform local`. It judges `HEAD` against the target branch (the manifest's
   `review.target`, else `origin/HEAD`, else `main`; fetch it first so it is current) and writes
   `.lockflight/local/ci/last.json`. Read the verdict line and the two contexts (`gates`, `lockflight/judge`):
   - **PASS** — CI should pass too. Say so, then open or push the pull request only if the user asked for it.
   - **FAIL** — a check found a problem. **NOT-EARNED** — a required check could not vouch (no red commit for a change
     that needs one, no spec to trace, a review that has not run). **ERROR** — a tool broke, not the code.
4. **Explain** anything but PASS with `.lockflight/bin/lockflight ci explain` (no operand: the local run). Per check it gives
   the detail, the reason code, the Next line and the link. Fix what is the agent's to fix, following the Next line, commit,
   and run the judge again. Hand to the user what the Next line gives to a person (an amend, a baseline, a review dismissal,
   an approval, a policy change on the target branch); never get around it.
   - A red commit already pushed is never rewritten: make a new red with `/lockflight:fix` (`lockflight red --redo <n>`) or
     `/lockflight:feature`.
   - `REVIEW_NOT_RUN` locally means only that no CI review ran here; CI's review job runs it when the class requires one.
5. **Never** call the branch ready while the local judge is not PASS, and never push with `--no-verify` on your own.

The local verdict can still differ from CI's when the target branch moved since the fetch, or the machine differs (OS,
culture, SDK). After the push, `/lockflight:explain-ci` with the run's link explains CI's verdict.
