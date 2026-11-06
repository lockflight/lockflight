# Fix a bug

A pull request that changes production code needs a **red commit**: a commit, before the fix, whose new tests fail because of
the code as it is, marked with a `Lockflight-Red: <n>` trailer. The CI judge builds that commit again and runs its new tests;
the trailer only says where to look. A bug fix needs nothing more: no spec, no approval and no red/green session.
`lockflight red` makes the red commit, after checking it the way the judge will. For people fixing a bug in a Lockflight
project, with an agent or in Visual Studio or Rider.

**On this page:** [The shape of a fix](#the-shape-of-a-fix) · [With an agent](#with-an-agent) ·
[In Visual Studio or Rider](#in-visual-studio-or-rider) · [From a terminal](#from-a-terminal) ·
[When the red is rejected](#when-the-red-is-rejected) · [When it is refused](#when-it-is-refused) ·
[After the red](#after-the-red) · [What it does not prove](#what-it-does-not-prove)

## The shape of a fix

1. **A test that reproduces the bug**, in an existing test file of the project the bug is in, with no change to production
   code yet.
2. **`lockflight red`**: the test must fail on the code as it is, in production code (RED_ASSERT, or RED_STUB for a stub). Then
   it is committed with `Lockflight-Red: 1`.
3. **The fix**, as small as it can be, in a normal commit.
4. **The pull request.** The judge re-runs the red commit and then runs the whole suite at the head. A one-line fix with its
   test is `fix` class ([risk routing](risk-routing.md)): `red`, `tests.sinceRed` and `green` are required, a spec and
   approvals are not.

The order matters. A test written after the fix passes on the red commit's code, and the judge says GREEN_BEFORE: the test does
not show that anything was broken.

## With an agent

In Claude Code with the plugin, describe the bug ("the discount is wrong for orders over 100, fix it"). The `fix` skill
writes the test first, runs `lockflight red`, changes the test (never the code) until the red is accepted, fixes the code,
runs the gates, checks the class with `route show --head HEAD` and runs the local judge (`lockflight ci --platform local`).
It works in lite and standard projects.

The skill stops and hands over to `/lockflight:feature` when the branch belongs to a feature: when the feature the branch,
`.specify/feature.json` or a Spec Kit variable names has a spec, or when its red/green session is open. It also stops when the
change turns out to be a feature (the class needs a spec), and it asks you before it commits a red with `--no-verify` past a
pre-commit hook that runs the tests.

## In Visual Studio or Rider

The engine runs from any terminal: PowerShell, `cmd`, or the IDE's own terminal. It needs Git for Windows on `PATH`, not Git
Bash, Claude Code or the plugin.

1. Write the test, and run it in the Test Explorer to see it fail.
2. Run `dotnet lockflight red` in the terminal, from the solution's directory (`.lockflight\bin\lockflight.exe red` works too
   where the engine is linked). Its output names each new test with its class and prints a `dotnet test` line that runs the
   same tests in your checkout.
3. Fix the code, run the tests, commit the fix and push.

To run it from a menu, add an external tool:

- **Visual Studio:** Tools → External Tools → Add. Command `dotnet`, arguments `lockflight red`, initial directory
  `$(SolutionDir)`, with "Use Output window" checked.
- **Rider:** Settings → Tools → External Tools → Add. Program `dotnet`, arguments `lockflight red`, working directory
  `$SolutionDir$`.

The output is plain text. A build error keeps its `file(line)` form, which the output pane links.

The tree is built in a worktree of its own (`lf-red`, in the repository's git directory), not in your checkout, so the test
host holding your `bin/` folder does not get in the way. The first run builds everything; later runs are incremental.

## From a terminal

```sh
lockflight red                       # judge the working tree as a red, and commit it when accepted
lockflight red --dry-run             # judge only
lockflight red --message "test: discounts round half to even"
lockflight red --base origin/release/2.1   # compare with another target branch
```

What goes in is the working tree as it is, untracked source files included, written as a commit without touching your index
or branch. It is compared with the **fork point**, the merge base with the target branch: `review.target`, else the branch
`origin/HEAD` names, else `main`, taken as `origin/<name>` when that exists. A line says so when your local branch differs from
it, because CI forks from the remote tip. Earlier reds on the branch are not new tests again; the second red of a branch is
labelled `2`.

The output says which production files the red holds, or that production is unchanged since the fork point. For a fix,
"unchanged" is what you want: the test fails on the target branch's code.

## When the red is rejected

Nothing is committed, and each new test's line says why it got its class.

| Class | What it means for a fix |
|---|---|
| GREEN_BEFORE | The test passes on the code as it is: it does not reproduce the bug, or the fix is already in the tree. Make it assert what is wrong now; if the fix is in your working tree, stash it first. |
| COMPILE_RED | The test does not compile. A fix tests code that exists; a new parameter needs a stub (`throw new NotImplementedException()`) in the red. |
| RED_IN_TEST | The test failed in its own code, not on an assertion about production code. |
| RED_NO_PRODUCTION | The test fails without calling production code. |
| SKIPPED, FLAKY | The test did not run, or passed on a second run. |

A test deleted, newly skipped or made `Explicit` since the fork point refuses the red too.

## When it is refused

| Refusal | What to do |
|---|---|
| A feature's red cycle is open, or the branch's feature has a red/green record | The tests belong to that feature: use `redgreen red` (`/lockflight:feature`). |
| In a Claude Code session, the branch's feature has a spec | The same: an agent works on a feature through its lifecycle. |
| The branch is at or above the class that needs a spec (`routing.require.specFrom`, from the target branch's policy) and has no spec | The change is a feature: write the spec and use the feature flow. |
| Over `ci.maxRedCommits` | Squash earlier reds, or split the pull request. |
| Detached HEAD, a merge or rebase in progress, stray untracked files | As the message says. |
| A workspace shell | Run it in the unit repository whose tests you changed. |

## After the red

- **Redo a red before you push:** `git reset --soft HEAD~1`, change the test, run `lockflight red` again.
- **Redo a red the judge rejected after the push:** change the test, then `lockflight red --redo <n>`. The new commit carries
  label `n` again and supersedes the rejected one.
- **Do not edit the test after its red.** The judge checks every cycle test that changed since its red on the red commit's
  code: one that passes there fails `tests.sinceRed`, and so does one with fewer data rows.
- **A pre-commit hook that runs the tests** refuses a red commit by nature. The red stays accepted, and the output gives the
  `git commit --no-verify` line to make the commit yourself. A hook that changes files (a formatter) makes the commit differ
  from what was judged: the commit is undone, and you run `lockflight red` again on the formatted files.
- **Check the whole branch** with `lockflight ci --platform local` once the fix is committed, before you push: it runs the CI
  judge's checks with the target branch's policy and gives the verdict and reason codes CI gives for the same commits. When it
  does not pass, `lockflight ci explain` lists each check with its reason, what to do next and its entry in
  [reasons](reasons.md). With the plugin, the `check-pr` skill does both.

## What it does not prove

`lockflight red` is a convenience. The judge re-runs every red commit from scratch, and where the two differ the judge wins:
the target branch moved, the local `origin/<target>` was stale, or the CI runner's OS, culture or SDK differ.

A red shows that a test fails in production code before the fix and passes after it. It does not show that the assertion is
the bug: a test that asserts the wrong thing, a red that breaks production code so that the "fix" restores it, or a stub in
existing code can all pass. The red commit's diff shows production changes, and the review judges the rest
([guardrails and boundaries](guardrails-and-boundaries.md#lockflight-red-and-the-feature-flow)).

## Next

- [Commands](commands.md#fixes-any-profile): every option and exit code of `red`
- [Risk routing](risk-routing.md): which class a pull request is in, and what it requires
- [Troubleshooting](troubleshooting.md#lockflight-red-says-one-thing-and-the-ci-judge-another): when the local red and CI disagree
