# Adopting an existing setup

Read this when the project already has its own Claude Code setup (hooks in `.claude/settings.json`, `.claude/hooks`, skills,
agents), Spec Kit (`.specify/`) or features in `specs/`. `init` adopts it: there is no separate step.

1. Ask the user to commit or stash their own changes first, so init's changes become one commit of their own.
2. Run init with the options you chose and `--dry-run` (`~/.lockflight/bin/lockflight init --dry-run --profile …`). It writes
   nothing and prints the files it would create or update and the adoption plan. Explain the plan by area:
   - **kept:** their hooks, skills and agents stay, and are now protected from you;
   - **protect:** the paths of their write-guard hooks are added to `protected:`, so shell commands are covered too;
   - **flag:**
     - a formatter that runs after edits can rewrite locked tests during a red cycle;
     - a Stop hook that builds runs next to the Stop gate;
     - a reviewer agent stays advisory, while `lockflight review` is the review of record;
   - **Spec Kit:** init leaves it as it is unless you pass `--speckit-preset <preset>`; then it upgrades Spec Kit with the
     Lockflight preset and keeps and lists the team's customised files;
   - **mark:** existing features predate Lockflight and need no approvals (only the init that creates `lockflight.yaml`
     marks them);
   - **add:** units (into a new `lockflight.yaml`; for an existing one init prints the lines for the user to add) and a
     short block in `CLAUDE.md`.
3. When the user agrees, run the same init without `--dry-run`. The Spec Kit upgrade (standard profile, when the user
   wants it) is theirs: `init --speckit-preset` is refused inside Claude Code. Give them the line to run **in a separate
   terminal window** (not with `!` in Claude Code), with absolute paths:
   `cd <project root> && .lockflight/bin/lockflight init --speckit-preset <preset>`. Find the preset with
   `echo "${CLAUDE_PLUGIN_ROOT}/speckit-preset"`, or else `ls -d ~/.claude/plugins/cache/lockflight/lockflight/*/speckit-preset`
   (take the newest version).
4. Review `git diff` with them. They commit init's changes as one commit. To undo init later, they run `git revert` on that
   commit; there is no other undo.

Never run `specify init` over an existing `.specify/`, and never run any Spec Kit command that writes `.claude/skills/**`
yourself once the project is linked: those files are protected.
