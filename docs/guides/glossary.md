# Glossary

Short definitions of the terms the other pages use, in the order you are likely to meet them.

| Term | Meaning |
|---|---|
| **Gate run** | One `lockflight gate run`: the steps of a profile, in order, ending in one verdict. |
| **Profile** | A named list of steps: `edit`, `stop` or `pr`. |
| **Step** | One deterministic check inside a gate run, such as `build`, `test`, `coverage.patch` or `security.secrets`. |
| **Verdict** | `PASS` (exit 0), `FAIL` (1), `ERROR` (3, the engine itself failed) or `NOT-EARNED` (5, the check could not judge, which is never a pass). A single step may also be `N/A`: it did not apply to the run and neither passes nor blocks. A run in which no step applied is `N/A` as a whole and exits 0 (for example `edit` with no changed C# lines). |
| **Scope** | The `Scope: …` line of a verdict: what the run actually judged — the number of tests, or for a run that stopped earlier what that step checked (such as `3 changed C# files (format)`). |
| **Phase check** | `lockflight phase check specify\|tasks`: a deterministic check that a lifecycle phase is complete. It is separate from gates. |
| **Feature** | A feature directory (`specs/<id>/`) holding `spec.md`, `plan.md` and `tasks.md`, written by hand, by the crew or by Spec Kit. Lockflight keeps its records for it in `.lockflight/features/<id>/`. |
| **Spec contract** | The format of `spec.md` and `tasks.md` that the phase checks read: `### User Story N - Title` sections with bold Given/When/Then scenarios, `- **FR-001**: …` requirements, task lines citing their requirements, tests before implementation in every story. See [the spec contract](spec-contract.md). |
| **Approval** | A record that binds the SHA-256 of approved artifacts (`spec.md`; `tasks.md` + `plan.md`) to a person, optionally SSH-signed. Any edit to an artifact makes its approval stale. |
| **Cycle** | One red/green round for a user story: `redgreen red` records the failing tests, `redgreen green` records them passing. |
| **RED_STUB / RED_ASSERT** | The accepted ways for a new test to fail: a `NotImplementedException` whose top stack frame is in production code (RED_STUB), or an assertion in a test that calls production code (RED_ASSERT). Recognised assertions: xUnit's `Assert` (v2 and v3), NUnit's `Assert.That` and classic asserts (`AssertionException`, `MultipleAssertException`, recognised from the TRX message), Shouldly (`ShouldAssertException`, `ShouldMatchApprovedException`, `ShouldCompleteInException`), FluentAssertions and AwesomeAssertions, NSubstitute `Received` / `Received.InOrder` (`ReceivedCallsException`, `CallSequenceNotFoundException`), Moq `Verify` and strict-mock failures (`MockException`) and Verify snapshot mismatches (`VerifyException`); a library's other exceptions report its misuse and are RED_OTHER or RED_IN_TEST. Rejected: failures in test code or its setup, `Assert.Fail` (xUnit and NUnit) and throwing fixtures (RED_IN_TEST), other exceptions or exceptions from the runtime or a library (RED_OTHER), assertions in a test that reaches no production member, where a type argument or `typeof` alone does not count (RED_NO_PRODUCTION), already-green, skipped (NUnit's `Assert.Ignore` and `Assert.Inconclusive` too) and flaky tests. A parameterized test (theory or `[TestCase]` rows) is judged by its worst row. |
| **Test lock** | While a cycle is red, the guard blocks edits to the test projects, so the agent can't change the tests to fit the code. |
| **Amend** | `lockflight redgreen amend <test> --reason "…"` (or `/lockflight:amend` typed in Claude Code): a person lets one test of the open red cycle change. The file that declares it is writable until the next `redgreen red` shows the test failing in production code again. |
| **Red-time snapshot** | The tree a red was accepted on, kept by `redgreen red` as a git commit object under `refs/lockflight/red/<feature>/<cycle>/<commit>`. An amended test is re-checked on it. The ref is local: Lockflight never pushes it, and a normal `git push` does not either. `git push --mirror` or a refspec for `refs/*` would, with any untracked file the snapshot holds. `redgreen reset` deletes the refs of the feature's cycles; delete any other with `git update-ref -d <ref>`. |
| **Trace** | The matrix from requirement (`FR-###`) to the tests that went red, then green, for it. |
| **Review of record** | `lockflight review`: an independent reviewer whose prompt the engine builds. Blocker and major findings count only when their reproduction test fails. |
| **Protected path** | A file or glob that only a human or a Lockflight command may change. The PreToolUse guard blocks agent writes to it; the agent proposes a change with `lockflight propose` instead. |
| **Guardrail / boundary** | A guardrail prevents accidents but can be circumvented from the agent's own shell. A boundary holds even against a determined agent. See [guardrails and boundaries](guardrails-and-boundaries.md). |
| **Baseline** | Accepted existing problems (build warnings, failing tests, advisories) for brownfield adoption. Only a human records them. |
| **Lockdown** | `.lockflight/lockdown` exists: only reads are allowed until a human removes it. |
| **Recovery mode** | The engine isn't linked yet: only reads and the restore command are allowed. |
| **In-session human command** | `/lockflight:approve`, `/lockflight:baseline`, `/lockflight:reset`, `/lockflight:amend`, typed by a person in Claude Code: step 1 shows what would be recorded with a one-time code, step 2 (the line with the code) records it. |
| **Human-only command** | `approve` (except `approve verify`), `baseline`, `redgreen reset`, `redgreen amend`, `review dismiss`, `adopt apply\|undo`, `update`: refused inside a Claude Code session; `--help` or `-h` on one only prints the usage and is allowed. |
| **Ledger** | Local JSON-lines event log (blocks, verdicts, rejected reds, confirmed findings) that `ledger retro` clusters. It is not an audit log. |
| **Lite / standard** | Onboarding profiles. Lite: guard and gates. Standard: adds the lifecycle (spec and tasks, approvals, red/green, trace, review); Spec Kit is optional in it. |
| **Crew** | The optional `lockflight-crew` plugin: a spec writer, a planner and a test author, each briefed for one step and judged by that step's engine check. An aid, not a judge. |

## Next

- [How it works](../architecture.md): how these parts fit together
- [Commands](commands.md): every command and its exit codes
