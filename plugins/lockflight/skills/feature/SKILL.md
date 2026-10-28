---
name: feature
description: Take a feature through Lockflight's gated lifecycle — Spec Kit specify/clarify, plan, tasks, then test-first implementation in red/green cycles, trace and gates. Use when the user asks to build, implement or add a feature or user story in a Lockflight project with the standard profile.
---

# Lockflight feature lifecycle

The engine judges every step; you never declare a step done while its check fails. Human gates are the user's decision: you
stop and ask, you never approve on the user's behalf.

Run the engine as `.lockflight/bin/lockflight <command>` from the project root (the directory holding `lockflight.yaml`): it
is the engine the hooks run, and it works whatever way Lockflight was installed.

**First, check the profile.** This lifecycle needs the standard profile. If the project root has no `lockflight.yaml`, or
no `.specify/` (a lite project), stop before writing anything: tell the user the project is not set up for the lifecycle,
offer the standard setup (`/lockflight:init`), or build the change without it and rely on the gates
(`/lockflight:gate`).

**Approvals.** Whenever a step below asks for an approval, the user gives it in one of two ways: they type
`/lockflight:approve spec` (or `tasks`) in this session, or they run `.lockflight/bin/lockflight approve spec|tasks` from
the project root in a separate terminal window (not with `!` in Claude Code: that runs inside the session and is refused).
Never approve for them.

1. **Specify.** Run `/speckit-specify` with the user's request (Spec Kit 1.1.3 creates `specs/<NNN-slug>/spec.md`, numbered
   `001`, `002`, …). Requirements are lines `- **FR-001**: …`; each user story has `**Given** … **When** … **Then** …`
   scenarios. Resolve open points with `/speckit-clarify`. Replace every template placeholder (`[FEATURE NAME]`,
   `[Brief Title]`, "System MUST [specific capability …]") and delete unused example lines. Then run
   `.lockflight/bin/lockflight phase check specify` until it passes.
2. **Human gate — spec.** Ask the user to review `spec.md` and approve it (`/lockflight:approve spec`, see Approvals). Wait.
3. **Plan and tasks.** Run `/speckit-plan`, then `/speckit-tasks`. Every story task cites its requirements `(FR-001)`; within a
   story the stub declaration comes first, then the test tasks, then the implementation. Tests are mandatory here, whatever
   the stock `/speckit-tasks` text says (the Lockflight preset appends these rules to it). Run
   `.lockflight/bin/lockflight phase check tasks` until it passes; a failure names each misplaced task, its line and why it
   counted as implementation. A task counts as a **test** when it sits under a `### Tests` heading, starts with test wording
   ("Write tests …", "Contract tests …", "Cover … with tests") or names a test file (`…Tests.cs`, `….Tests/`) without starting
   with "Implement" or "Make"; as a **stub** when it says stub, skeleton, `NotImplementedException` or "declare … signatures";
   anything else is **implementation**.
4. **Human gate — tasks.** Ask the user to approve the tasks (`/lockflight:approve tasks`, see Approvals). Wait.
5. **Start.** `.lockflight/bin/lockflight redgreen start` records the test inventory (once per feature) and runs the suite once.
   If it warns that tests already fail, tell the user: they decide whether to run `.lockflight/bin/lockflight baseline tests` in
   a separate terminal window (not with `!` in Claude Code: that runs inside the session and is refused), and merge that
   baseline in its own pull request first.
6. **For each user story, one red/green cycle:**
   1. Declare the members the tests need with bodies `throw new NotImplementedException();`.
   2. Write the story's xUnit tests (v3 or v2), each tagged `[Trait("Requirement", "FR-001")]`, asserting the specified behaviour.
   3. `.lockflight/bin/lockflight redgreen red`. Every new test (one not in the inventory `start` recorded) must fail because
      of production code: the stub's `NotImplementedException` (RED_STUB) or an assertion in a test that calls the production
      code (RED_ASSERT). If the red is rejected, fix the tests — never weaken an assertion to make a red "work". A test the
      output marks `assertion-free` checks nothing but that the code does not throw: add an assertion on the result unless
      the story's behaviour is the exception itself.

      | Class | Meaning | Fix |
      |---|---|---|
      | RED_STUB (accepted) | `NotImplementedException` thrown by production code | — |
      | RED_ASSERT (accepted) | an assertion fails in a test that calls production code | — |
      | COMPILE_RED | the tests do not build | declare the missing members as stubs |
      | RED_IN_TEST | the test, its fixture, constructor or `Assert.Fail` throws | call the production code and let it fail there |
      | RED_OTHER | another exception, or one thrown by the runtime or a library | stub the member, or assert on its result |
      | RED_NO_PRODUCTION | an assertion fails but the test reaches no production member (a type argument such as `Deserialize<T>`, or `typeof`, is not a call; a method, a constructor or a property read is) | call the member under test |
      | GREEN_BEFORE | the test passes before the code exists | test behaviour that is still missing |
      | SKIPPED | skipped or not run | remove `Skip` / `Explicit` |
      | FLAKY | repeated runs disagree | make the test deterministic |

      Under each test, `redgreen red` says why it got its class; `redgreen red --verbose` adds the exception, the deciding
      stack frame with its line, and the production member the test reaches.

      Assertions recognised: xUnit `Assert` (v2 and v3), Shouldly, FluentAssertions, AwesomeAssertions, NSubstitute
      `Received`, Moq `Verify`, Verify snapshots. Their other exceptions (misusing the library) are not assertions.

      **Optional test author.** If the agent type `lockflight-crew:test-author` is available (the `lockflight-crew` plugin
      is installed), delegate steps 2–3 for this story to it instead, in these steps; none of them may be skipped:
      1. Call the Agent tool with `subagent_type: "lockflight-crew:test-author"` and a prompt naming the feature directory
         and the story (for example `specs/001-shipping`, `US1`).
      2. **When it returns, your very next action MUST be `.lockflight/bin/lockflight redgreen status`, as a Bash call of
         its own,** before you summarize, edit, or run anything else, and whatever the agent reported, accepted or not.
         Quote the line for this story from its output in your reply. Why: the agent is an aid, not a judge. Its report is
         its own word, and a subagent can be wrong or stop early while sounding finished; the engine's record is the only
         evidence that the red was accepted.
      3. Go on only if that output shows a cycle for this story as `RED (test projects locked)`. A rejected red, a stop at
         its turn limit, or a report that it was blocked is a failed round: start a fresh one with the engine's last
         output and the agent's report in the prompt, at most two rounds in all, and run step 2 again after each.
      4. After two failed rounds, stop and give the user the last verdict and what the test author reported. If it reports
         a missing stub, declare only that member as in step 6.1 (body `throw new NotImplementedException();`) and
         start the next round; never implement it.

      Without that plugin, write the tests yourself as above.
   4. The test projects (`tests/**`, `test/**`, `*.Tests/**` and the like) are now locked: edits there are refused until
      green. Implement in production code until `.lockflight/bin/lockflight redgreen green` passes. If a test itself turns
      out wrong (its expectation contradicts the spec, or the API it uses had to change), stop and ask the user; never edit a
      locked test, and never amend or reset yourself. Tell them which test and why, and that they can let exactly that test
      change by typing `/lockflight:amend <Class.Method> --reason <why>` here, or with
      `.lockflight/bin/lockflight redgreen amend <Class.Method> --reason "<why>"` run in a separate terminal window (not with
      `!` in Claude Code: that runs inside the session and is refused). Never type the command or its code yourself. Check
      `.lockflight/bin/lockflight redgreen status`: it names the file the amend unlocks. Then change only what the amend
      allows, run `.lockflight/bin/lockflight redgreen red` (the amended test must fail in production code again), and go on
      to green. Discarding the whole cycle is `redgreen reset` (or `/lockflight:reset`), also theirs.
      **If a test written in this cycle turns out wrong after red, ask the user for `/lockflight:amend <test>`. Never change
      production code just to satisfy a wrong test, and `baseline` does not excuse this cycle's tests.**
   5. If `red` or `green` refuses because an approval is stale (spec.md, plan.md or tasks.md changed after approval), show the
      user what changed and ask them to approve again (see Approvals). Wait.
7. **Verify.** `.lockflight/bin/lockflight trace` (every requirement covered by a test that went red, then green) and
   `.lockflight/bin/lockflight gate run --profile pr --base <base branch>`.
8. **Review.** Run the review of record as the `review` skill describes (`.lockflight/bin/lockflight review`; leave out
   `--base`, `--model` and `--budget`). Fix confirmed findings in a new red/green cycle and review again; show unverified
   ones to the user. Then run `.lockflight/bin/lockflight review status`: only its output says which findings are open.
9. **Deliver.** Open the PR. Its body holds the trace table, the gate summary and the "Pull request summary" from
   `review status`, unchanged. Merging is always the user's decision.

If the project has no xUnit tests, `redgreen` reports NOT-EARNED (it could not judge): say so, follow the steps without
red/green, and rely on the gates.
