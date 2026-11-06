# lockflight-crew

**Experimental advice** for [Lockflight](../../README.md), test-first guardrails for AI coding agents on .NET. An optional
plugin, installed beside the `lockflight` plugin:

```text
/plugin install lockflight-crew@lockflight
```

- **Agents** that `/lockflight:feature` dispatches: a spec writer and a planner that write `spec.md`, `plan.md` and `tasks.md`
  in the [spec contract](../../docs/guides/spec-contract.md) until `lockflight phase check` passes, and a test author that
  writes a user story's failing tests until `lockflight redgreen red` accepts them.
- **Advice skills:** a rules audit and a retro over the local ledger, which read `lockflight facts --json` and judge nothing.

The crew decides nothing. Each agent's work is checked by the same engine checks as anyone's, the skill that dispatched it
runs that check again, and the CI judge decides at the pull request. The plugin ships no hooks, executables or policy files
(`scripts/crew-trust-boundary.sh` checks that). Its tool lists and briefs are a guardrail, not a boundary.

More: [the crew](../../docs/guides/skills.md#the-crew-optional).
