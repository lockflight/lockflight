---
name: feature
description: Take a feature through Lockflight's gated lifecycle — Spec Kit specify/clarify, plan, tasks, then test-first implementation in red/green cycles, trace and gates. Use when the user asks to build, implement or add a feature or user story in a Lockflight project with the standard profile.
---

# Lockflight feature lifecycle

The engine judges every step; you never declare a step done while its check fails. Human gates are the user's decision: you
stop and ask, you never approve on the user's behalf.

Run the engine as `.lockflight/bin/lockflight <command>` from the project root (the directory holding `lockflight.yaml`): it
is the engine the hooks run, and it works whatever way Lockflight was installed.

1. **Specify.** Run `/speckit-specify` with the user's request (Spec Kit 1.1.3 creates `specs/<NNN-slug>/spec.md`, numbered
   `001`, `002`, …). Requirements are lines `- **FR-001**: …`; each user story has `**Given** … **When** … **Then** …`
   scenarios. Resolve open points with `/speckit-clarify`. Replace every template placeholder (`[FEATURE NAME]`,
   `[Brief Title]`, "System MUST [specific capability …]") and delete unused example lines. Then run
   `.lockflight/bin/lockflight phase check specify` until it passes.
2. **Human gate — spec.** Ask the user to review `spec.md` and approve it with `/lockflight:approve spec`. Wait.
3. **Plan and tasks.** Run `/speckit-plan`, then `/speckit-tasks`. Every story task cites its requirements `(FR-001)`; within a
   story the stub declaration comes first, then the test tasks, then the implementation. Tests are mandatory here, whatever
   the stock `/speckit-tasks` text says (the Lockflight preset appends these rules to it). Run
   `.lockflight/bin/lockflight phase check tasks` until it passes; a failure names each misplaced task, its line and why it
   counted as implementation.
4. **Human gate — tasks.** Ask the user to approve with `/lockflight:approve tasks`. Wait.
5. **Start.** `.lockflight/bin/lockflight redgreen start` records the test inventory (once per feature) and runs the suite once.
   If it warns that tests already fail, tell the user: they decide whether to run `.lockflight/bin/lockflight baseline tests` in
   a separate terminal window (not with `!` in Claude Code: that runs inside the session and is refused), and merge that
   baseline in its own pull request first.
6. **For each user story, one red/green cycle:**
   1. Declare the members the tests need with bodies `throw new NotImplementedException();`.
   2. Write the story's xUnit v3 tests, each tagged `[Trait("Requirement", "FR-001")]`, asserting the specified behaviour.
   3. `.lockflight/bin/lockflight redgreen red`. Every new test must fail inside production code (RED_STUB or RED_ASSERT). If
      the red is rejected, fix the tests — never weaken an assertion to make a red "work".

      **Optional test author.** If the agent type `lockflight-crew:test-author` is available (the `lockflight-crew` plugin
      is installed), delegate steps 2–3 for this story to it instead: the Agent tool with
      `subagent_type: "lockflight-crew:test-author"` and a prompt naming the feature directory and the story (for example
      `specs/001-shipping`, `US1`). It is an aid, not a judge: whatever it reports, run
      `.lockflight/bin/lockflight redgreen status` yourself and go on only if a cycle for this story is now
      `RED (test projects locked)`. A rejected red, a stop at its turn limit, or a report that it was blocked is a failed
      round; start a fresh one with the engine's last output in the prompt, at most two rounds in all. After two failed
      rounds, stop and give the user the last verdict and what the test author reported. Without that plugin, write the
      tests yourself as above.
   4. The test projects are now locked. Implement in production code until `.lockflight/bin/lockflight redgreen green` passes.
      If a test itself turns out wrong, stop and ask the user: only they can unlock, with
      `.lockflight/bin/lockflight redgreen reset` run in a separate terminal window (not with `!` in Claude Code: that runs
      inside the session and is refused).
   5. If `red` or `green` refuses because an approval is stale (spec.md, plan.md or tasks.md changed after approval), show the
      user what changed and ask them to approve again (`/lockflight:approve`): they run the command in a separate terminal
      window. Wait.
7. **Verify.** `.lockflight/bin/lockflight trace` (every requirement covered by a test that went red, then green) and
   `.lockflight/bin/lockflight gate run --profile pr --base <base branch>`. Put the trace table and the gate summary into the PR
   body.
8. **Deliver.** Open the PR; merging is always the user's decision.

If the project has no xUnit v3 tests, `redgreen` reports NOT-EARNED: say so, follow the steps without red/green, and rely on the
gates.
