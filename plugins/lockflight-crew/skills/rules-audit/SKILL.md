---
name: rules-audit
description: Audit the rules written in this repository's agent instructions (CLAUDE.md, .claude/rules, skills, the Spec Kit constitution) against what actually backs them, from `lockflight facts --json`, and advise how to back the important ones with a deterministic check. Use when the user asks which of their rules are enforced, for a rules audit, or whether their CLAUDE.md rules are checked by anything.
---

# Rules audit (advice)

Everything this skill says is **advice**, not a verdict: no gate reads it, and "backed" never means "enforced". Say so once at
the top of your answer.

1. **Facts first.** From the project root run `.lockflight/bin/lockflight facts --json` (or `lockflight facts --json` when the
   project is not linked). Read the JSON; its shape is documented under `facts` in docs/guides/commands.md. If it fails with
   "not a git repository", say the audit reads tracked files and stop. Use only these facts and the files they name: do not
   guess at configuration the facts do not show.
2. **Collect the rules.** Read each file in `instructionFiles`. A rule is an imperative sentence or list item (never, always,
   must, do not, don't, only, avoid). Skip fenced code, headings and descriptions. Keep the file and line of each.
3. **Match each rule to what backs it, by exact matches only:**
   - **checked (guardrail):** a ban on editing a path that a glob in `protectedPaths` covers (the guard refuses the agent's
     writes); a ban on running a command listed in `humanOnlyCommands` (the engine refuses it inside Claude Code).
   - **related:** the rule names an analyzer id (`CA1502`, `IDE0005`), or a metric a code metrics rule measures ("cyclomatic
     complexity" CA1502, "maintainability index" CA1505, "class coupling" CA1506), and `analyzers` has that id with
     `reachesBuild: true` (the build gate fails a new warning, only in the projects the setting reaches); the rule bans an API
     that a `bannedSymbols` file lists (RS0030 is a warning: only a new warning against a build baseline fails); the rule bans
     a dependency between two named layers (Domain, Application, Infrastructure, Api) and `architectureTests.present` is true
     (the test checks its own rules, not this sentence). Quote the `codeMetrics` threshold when one applies.
   - **guidance only:** nothing above matches. Most rules are, and that is fine for rules about judgement.
   `manifest: null` means Lockflight is not set up: nothing is checked by the guard.
4. **Report** grouped per file, each rule with its line, its label and what backs it. Never write "enforced" or "boundary":
   these are guardrails and build warnings on the developer's machine; CI with the base branch's policy is what a team can rely
   on (docs/guides/guardrails-and-boundaries.md).
5. **Advise** for the rules that matter most (bans, safety, architecture), the most deterministic backing first:
   - a path ban → add the path to `protected:` in `lockflight.yaml`;
   - an analyzer or metric → `dotnet_diagnostic.<id>.severity = warning` in `.editorconfig` (`lockflight init --quality` sets
     CA1502, CA1505 and CA1506), then a build baseline;
   - an API ban → a `BannedSymbols.txt` entry with BannedApiAnalyzers (`T:`, `M:` or `P:` with the full namespace; say the id
     is not verified);
   - a layer ban → an architecture test (`lockflight init --architecture-tests`);
   - a rule that is really a check → a test or a CI step; only judgement belongs in prose.
6. **Never change** `lockflight.yaml`, analyzer configuration, `BannedSymbols.txt` or any other protected file yourself. Write
   the proposed file in your scratch directory and run `.lockflight/bin/lockflight propose <path> --from <file>`; the user
   reviews and applies it, and a change to policy goes through their pull request review.
