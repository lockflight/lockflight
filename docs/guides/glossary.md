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
| **Feature** | A Spec Kit feature directory (`specs/<id>/`). Lockflight keeps its records for it in `.lockflight/features/<id>/`. |
| **Approval** | A record that binds the SHA-256 of approved artifacts (`spec.md`; `tasks.md` + `plan.md`) to a person, optionally SSH-signed. Any edit to an artifact makes its approval stale. |
| **Cycle** | One red/green round for a user story: `redgreen red` records the failing tests, `redgreen green` records them passing. |
| **RED_STUB / RED_ASSERT** | Accepted ways for a new test to fail: a `NotImplementedException` thrown from production code, or an assertion failure. Failures inside test code, already-green tests, skipped tests and flaky tests are rejected. |
| **Test lock** | While a cycle is red, the guard blocks edits to the test projects, so the agent can't change the tests to fit the code. |
| **Trace** | The matrix from requirement (`FR-###`) to the tests that went red, then green, for it. |
| **Review of record** | `lockflight review`: an independent reviewer whose prompt the engine builds. Blocker and major findings count only when their reproduction test fails. |
| **Protected path** | A file or glob that only a human or a Lockflight command may change. The PreToolUse guard blocks agent writes to it; the agent proposes a change with `lockflight propose` instead. |
| **Guardrail / boundary** | A guardrail prevents accidents but can be circumvented from the agent's own shell. A boundary holds even against a determined agent. See [guardrails and boundaries](guardrails-and-boundaries.md). |
| **Baseline** | Accepted existing problems (build warnings, failing tests, advisories) for brownfield adoption. Only a human records them. |
| **Lockdown** | `.lockflight/lockdown` exists: only reads are allowed until a human removes it. |
| **Recovery mode** | The engine isn't linked yet: only reads and the restore command are allowed. |
| **Human-only command** | `approve`, `baseline`, `redgreen reset`: refused inside a Claude Code session. |
| **Ledger** | Local JSON-lines event log (blocks, verdicts, rejected reds, confirmed findings) that `ledger retro` clusters. It is not an audit log. |
| **Lite / standard** | Onboarding profiles. Lite: guard and gates. Standard: adds Spec Kit and the lifecycle. |

## Next

- [How it works](../architecture.md): how these parts fit together
- [Commands](commands.md): every command and its exit codes
