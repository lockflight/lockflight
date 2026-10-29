---
name: retro
description: Run a retro over Lockflight's local ledger — recurring hook blocks, failing gates, rejected reds and confirmed review findings, clustered with a suggested remedy for each, as advice. Use when the user asks what keeps going wrong, for a retro, or for lessons learned from Lockflight's ledger.
---

# Retro (advice)

The ledger is local and best-effort, a signal rather than an audit record, and everything this skill concludes is **advice**:
no gate reads it and the remedies are changes a person reviews. Say so once at the top of your answer.

1. **Find the ledger.** From the project root run `.lockflight/bin/lockflight facts --json` and read `ledger`: `directory`,
   `schema` (`lockflight.ledger/v1`) and `files` (one JSON-lines file per month). No files: say there is nothing to review yet
   and stop. The window is the last 30 days unless the user asks for another; a fingerprint recurs at 2 occurrences unless
   they say otherwise.
2. **Read the lines** in the window (`ts` is the time). Each has `type`, `signature` (the fingerprint), `summary` and optional
   fields (`trigger`, `cache`, `durationMs`, `ofRecord`, `costUsd`, and who did what on a hook block). Count as **incidents** only:
   - `hook.blocked`;
   - `gate.completed` whose `signature` has no `:PASS`, that is not a per-unit Stop timing line (`gate:stop-unit:…`) and whose `trigger` is
     not `stop-hook` (a red Stop gate is already counted as its hook block);
   - `evidence.recorded` with `redgreen:red:rejected`, `redgreen:red:COMPILE_RED`, `review:changes` or `review:unverified`;
   - `review.completed` with `review:changes` or `review:unverified`.
   For `redgreen:red:rejected`, refine the fingerprint with the class after the last `: ` in `summary`
   (`redgreen:red:rejected:RED_IN_TEST`). Not incidents: passing gates, accepted reds and greens, approvals, configuration
   writes, fixed or dismissed findings, rejected greens (normal test-first iteration), session-command answers.
3. **Activity** in a few lines: gate runs (how many PASS), Stop hook gates (how many from the cache, total seconds from
   `durationMs`), hook blocks, reds (accepted), greens, approvals, reviews (of record, cost from `costUsd`),
   findings fixed and dismissed.
4. **Recurring incidents**, most frequent first, as a table: fingerprint, count, last seen, class, suggested rung. Classify by
   the first match:

   | Fingerprint contains | Class | Rung | Suggestion |
   |---|---|---|---|
   | `protected-path:` | rule known, agent reached for it anyway | skill | say in the relevant skill which lockflight command changes the file, or why it is protected |
   | `human-only-command` | rule known, agent reached for it anyway | skill | make the skill stop and ask the user earlier |
   | `tests-locked:` | rule known, agent reached for it anyway | skill | check whether the reds were weak (review); reinforce "fix production code" |
   | `:ERROR` or `timed-out` | tooling gap | environment | run `lockflight doctor`; fix the setup or the timeout |
   | `:NOT-EARNED` | tooling gap | check | nothing was judged (no tests, no gitleaks, no report); complete the gate configuration |
   | ends with `:format` | missing knowledge | analyzer | format on edit, or an `.editorconfig` rule enforced in the build |
   | ends with `:build` | missing knowledge | analyzer | find the diagnostic ids; prefer an analyzer setting or code fix over instructions |
   | `:test` | missing knowledge | check | find the module the failures cluster in; add a check or a path rule there |
   | `coverage.patch` | process skipped | skill | reinforce test-first in the feature skill |
   | `security.` | missing knowledge | check | a pre-commit check or a path rule for the affected area |
   | starts with `redgreen:red:` | process skipped | skill | reinforce stubs first and assertion-based tests |
   | `review:changes` | missing knowledge | check | keep the reproductions as regression tests |
   | anything else | missing knowledge | check | inspect the examples and choose the most deterministic rung |

   Quote the latest `summary` of each. When `facts --json` shows the remedy already exists (an analyzer with
   `reachesBuild: true`, a protected path, a step in `fail` mode), say so instead of suggesting it again.
5. **For each incident the user wants to act on**, propose a concrete change at the most deterministic rung that would have
   prevented it: an analyzer or build setting, a check or a test, a skill update, a path-scoped rule — and only as a last
   resort an always-on rule. Draft it as a normal pull request for the user to review.
6. **Never change** gates, hooks, `lockflight.yaml`, protected paths or always-on rules yourself: those always need the user.
   For a protected file, write the proposal in your scratch directory and run
   `.lockflight/bin/lockflight propose <path> --from <file>`.
