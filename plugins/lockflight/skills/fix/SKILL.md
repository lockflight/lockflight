---
name: fix
description: Fix a bug test-first without a spec — reproduce it in a failing test, make the red commit with `lockflight red` (checked the way the CI judge checks it), then fix the code minimally, gate it and run the local judge. Use when the user says something is broken, wrong or a regression, or asks to fix a bug, in any Lockflight project (lite or standard). Not for adding, building or implementing something new: that is the feature skill.
---

# Lockflight fix

A fix needs a red commit like any change to production code: a test that fails on the code as it is, committed before the
fix, with a `Lockflight-Red` trailer the CI judge re-runs. It needs no spec, no approval and no red/green session.
`lockflight red` makes that commit after checking it the way the judge will.

Run the engine as `.lockflight/bin/lockflight <command>` from the project root (the directory holding `lockflight.yaml`). If
the project has no `lockflight.yaml`, say so and offer `/lockflight:init`.

1. **Reproduce.** Write the smallest test that shows the bug, in an existing test file of the project the bug is in. Change
   **no production file** before the red: the strongest evidence is a test that fails on the target branch's code.
2. **Red.** Run `.lockflight/bin/lockflight red`.
   - Accepted (exit 0): it committed the tests with `Lockflight-Red: <n>`. The output must say `Production unchanged since the
     fork point`; if it names production files, you changed production code before the red: say so to the user.
   - Rejected (exit 1 with a class): read the class and its fix line, change the **test**, never production code, and run it
     again. `GREEN_BEFORE` means the test does not reproduce the bug: the code already does what it asserts. `COMPILE_RED`
     means the test uses a member that does not exist: a fix tests existing code. `RED_IN_TEST` means the test failed in its
     own code, not on an assertion about production code.
   - Refused because the branch's feature has a spec or a red/green record, or a feature's red cycle is open: this work
     belongs to the feature lifecycle. Stop and hand over to `/lockflight:feature`.
   - Refused because the branch's class needs a spec: the change is a feature, not a fix. Stop and explain it to the user.
   - A commit hook refused the red commit (exit 0, with a `git commit --no-verify` line): ask the user whether to run that
     line. Never run `--no-verify` without the user's yes.
3. **Fix** the production code, minimally: the change that makes the new test pass and nothing else. Do not edit the test
   now; an edit after its red is judged again by CI and usually fails it.
4. **Gate.** Run `.lockflight/bin/lockflight gate run` until it reads PASS, then commit the fix with a normal message.
5. **Route.** Run `.lockflight/bin/lockflight route show --head HEAD`. If the class is at or above the one that needs a spec
   (the "needs" line lists a spec), stop: the change is a feature and needs a spec, an approval and `/lockflight:feature`.
   Do not push.
6. **Local judge.** Run `.lockflight/bin/lockflight ci --platform local` on the committed branch and report its verdict;
   when it is not PASS, `.lockflight/bin/lockflight ci explain` gives each check's reason and what to do next.
   - `red` FAIL on a cycle that is not pushed yet: `git reset --soft HEAD~1` undoes the red commit only if it is the last
     one; otherwise tell the user. On a pushed cycle, fix the test and run `.lockflight/bin/lockflight red --redo <n>`.
   - `tests.sinceRed` FAIL: the test changed after its red. Restore it, or redo the red.
7. **Never** type a `Lockflight-Red` trailer by hand, never commit with `--no-verify` on your own, and never call the fix
   done while the gate or the local judge is not PASS.

The local judge is advice; the CI judge on the pull request is the verdict of record. A red can differ there when the target
branch moved or the machine differs (OS, culture, SDK).
