# Guardrails and boundaries

Lockflight separates two kinds of control and labels every feature with one of them:

- A **guardrail (G)** prevents accidents and makes the right path the easy one. A determined agent — or a person — can get
  around it, because it runs on the same machine, with the same credentials, as the agent.
- A **boundary (B)** holds even against a determined agent, because the decision is made somewhere the agent cannot reach.

The agent in Claude Code runs with **your** credentials (your git, your `gh` token, your SSH agent). Anything you can do from
that shell, it can do. That fixes what Lockflight can honestly promise.

| Control | Label | Why |
|---|---|---|
| PreToolUse guard on protected paths (Edit/Write, Bash and PowerShell write detection) | G | best-effort parsing of shell commands; a script file or an interpreter can hide a write |
| Human-only commands refused inside Claude Code (`approve`, `baseline`, `redgreen reset`) | G | detected via `CLAUDECODE=1` and command tokens; the variable can be unset |
| Stop gate at the end of a turn | G | the agent's own session; Claude Code caps continuations |
| Test lock during a red cycle | G | applied by the guard (above); see the shell commands it recognises below |
| Red/green evidence | G | runtime evidence from commands the agent runs; it does not prove the order of history |
| Trace | G | built from that evidence |
| Review of record (`lockflight review`) | G | independent prompt, but the agent may skip running it |
| Ledger and retro | G | local and fail-open; never an audit log |
| **CI gates with the base branch's policy** | **B** where the check is **required** (rulesets/branch protection); otherwise advisory | runs on GitHub, with policy the PR cannot change |
| **Signed approvals verified in CI** | **B** only when all hold: presence-gated key (Touch ID / security key), allowed signers from the base branch, a required check, and a policy requiring approvals for the change | a file key without a passphrase could be used by the agent. Not in the generated workflow: add `approve verify` as shown in [CI hardening](ci-hardening.md) |
| Merge | G (the agent could use your token); B only with a separate identity for the agent | |

## What the shell write detection sees

The guard parses Bash and PowerShell commands for writes. Commands are split on `;`, `&&`, `||`, `|`, `&` and **newlines**
(`\n` and `\r\n`); subshells `( … )`, groups `{ …; }`, `if`/`for`/`while` bodies, PowerShell script blocks `& { … }` and the
commands inside `$( … )`, backticks and `<( … )` are each checked as commands of their own. Here-document bodies and comments are
not commands. Prefixes that run the next word as a program are looked through: `command`, `env [VAR=…]`, `sudo [-u …]`, `time`,
`nice`, `nohup`, `exec`, `timeout`, `busybox`, `!`, and `VAR=value` assignments. The command strings of `bash -c`/`sh -c`,
`eval`, `pwsh -Command`, `Invoke-Expression`, `cmd /c` and `Start-Process` are parsed too. ANSI-C quoting (`$'…'`) is decoded
and brace lists (`secrets/{a,b}`) are expanded.

It recognises:

- **Redirections** in every form: `>`, `>>`, `>|`, `2>`, `2>>`, `N>`, `&>`, `&>>`, `>&file` (`2>&1` and other fd copies write
  nothing).
- **File writers:** `tee`, `rm`, `unlink`, `shred`, `truncate`, `touch`, `chmod`/`chown`, `dd of=`, `curl -o`, `wget -O`.
- **Copies and moves:** `cp`, `mv`, `install`, `ln`, `rsync`. The destination counts as written, and so does
  `DEST/<name of each source>` — also with `-t DIR`/`--target-directory=DIR` and `.` as the destination. When the copy can create
  a tree (`cp -r`, every `mv`, `rsync`) or the source names are unknown (`$VAR`), everything under the destination counts.
  `mv` also removes its sources.
- **In-place editors:** `sed` with `-i` in any spelling (`-i`, `-i.bak`, `-Ei`, `-ni`, `--in-place[=suffix]`) and `perl -i`
  (`-pi`, `-0pi`, `-i.bak`).
- **Wildcards** (`*`, `?`, `[…]`) in any target: checked as the shell would expand them (the entries that exist) and as written.
  `rm -rf *` at the root is therefore blocked.
- **Writers whose files are named elsewhere:** `tar x` (`-C dir`), `unzip` (`-d dir`), `patch` (`-d dir`), `git apply`,
  `git worktree add` and `find … -exec rm` rewrite anything under their directory, which is checked as a tree. A
  `find … -name P -delete` deletes only names matching `P`, so it is blocked only when an existing file of that name below the
  directory is protected.
- **Agent worktrees.** Claude Code runs a subagent with `isolation: worktree` in `.claude/worktrees/<name>`, a full checkout of
  the project. The guard applies every rule there too, anchored at the worktree's root, so the worktree's copies of the
  manifest, the evidence and locked tests are protected like the originals.
- **Targets the command line does not show** are blocked: a path entirely computed by a command substitution or a PowerShell
  expression (`rm $(cat list)`, `> $(cmd)`, `Remove-Item (Join-Path secrets k)`), a writer fed by `xargs` (`… | xargs rm`) or, in PowerShell, by the
  pipeline (`… | Remove-Item`), .NET file APIs (`[IO.File]::WriteAllText`) and `pwsh -EncodedCommand`. One exception: a file
  list from `grep -l`/`-L` or `find` names the paths it searches, so `grep -rl Foo web | xargs sed -i …` counts as rewriting
  anything under `web`, and is allowed when no protected file lies there.
- **Partly computed targets:** a substitution inside literal text stands for a `*` in its segment (`logs/$(date +%F).log` is
  `logs/*.log`), so `touch "build-$(date +%s).log"` is allowed and `echo x > "secrets/$(date).log"` is blocked; one that starts
  the target (`"$(pwd)/x"`) is also judged like a variable.
- **Variables in a target:** `$PWD`, `$HOME` and `~` are resolved. A target built from another variable is blocked only when the
  literal part after it could be a protected path (`"$X/secrets/k"`, `$DIR/lockflight.yaml`); `"$TMPDIR/build.log"` or a bare
  `"$FILE"` are allowed, because the guard cannot follow variables.
- **git commands that rewrite files** the agent did not name as edits: `git checkout [<rev>] [--] <paths>` (a single operand
  such as `git checkout test` or `git checkout .` counts as a path; a branch name that is no path in the working tree matches
  nothing), `git checkout -f`, `git switch -f`, `git restore`, `git stash [push] [-- <paths>]`, `git stash pop|apply`,
  `git reset --hard`, `git clean -f`, `git read-tree -u`, `git rm`, `git mv`.
- **`dotnet format`:** its `--include` files, its project or solution directory, or the whole current directory (source files
  only).
- **PowerShell:** `Set-Content`, `Add-Content`, `Clear-Content`, `Out-File`, `Tee-Object`, `Set-Item`, `New-Item`, `Export-Csv`,
  `Export-Clixml`,
  `Remove-Item`, `Copy-Item` (into a directory, a tree with `-Recurse`), `Move-Item` and `Rename-Item` (directories as trees),
  their aliases, and parameters in every form (`-Path x`, `-Path:x`, `-LiteralPath`, `-FilePath`, `-Destination`, unambiguous
  prefixes such as `-Dest`).

**Directories.** A relative path is always checked as written (against the session's working directory); after a `cd <dir>`
(or `pushd`, `Set-Location`) it is also checked under that directory. A `cd` can only add paths to check, never remove one, so a
`cd` that fails or runs in a subshell or pipeline cannot hide a write. After a `cd` whose target cannot be known — one with `$`,
backticks or a substitution, `~`, a glob, `cd -` or `popd` without a matching `pushd` — a relative path could lie in any
directory, so the command is blocked when that path could match any protected rule anywhere. Because `.lockflight/**` is
protected, that is every relative write: use a literal `cd` target or a path from the project directory.

A command that may rewrite a whole directory (`git restore .`, `git checkout -- <dir>`, `rm -r`, `git clean`) is blocked when a
locked test project or a protected path could lie inside it; for rules without a fixed directory
(`**/Directory.Build.props`) the files that exist there are checked. A directory is walked only for rules that could match
below it by pattern (`secrets/**` never sends a walk into `node_modules`), the walk keeps only files whose name fits such a rule,
and a wildcard over many sibling directories shares one walk of their common parent. Because `lockflight.yaml` sits at the root, whole-repository
rewrites such as `git stash`, `git restore .` or `git reset --hard` are always blocked: name the files instead. A tree that contains the
project (`rm -rf ..`, `rm -rf ~` for a project under the home folder) is blocked without a scan. `dotnet format`
rewrites source files only, so it is checked against rules that can match source files.

It does **not** see:

- writes made by **interpreters and programs that write on their own**: `python -c "open(…, 'w')"`, `node -e`, `ruby -e`,
  build tasks, test runs, code generators, package managers;
- **script files** (`./fix.sh`, `bash fix.sh`, `source x`, `. ./x.ps1`) — only command strings given inline are parsed;
- **paths held in variables** whose literal part does not look protected (`rm "$FILE"`);
- **git commands that switch the working tree to committed states**: `git switch <branch>`, `git checkout <branch>`, `git merge`,
  `git pull`, `git rebase`, `git cherry-pick`, `git am`. They can change protected files, but only to content that was committed
  and is reviewed where it was committed (the CI protected-path check reads the base branch's policy);
- a protected file that a `git checkout <rev> -- <dir>` would recreate after it was deleted.

Paths are compared after collapsing `..` and then resolving symbolic links, so `link/../x` is judged as `x` even when `link`
points elsewhere. That is why it is a guardrail: it stops the common accidents, not a determined agent.

## GitHub plan limits

On **GitHub Free, private repositories cannot require status checks**: the Lockflight workflow runs and reports, but a red check
does not block a merge — it is advisory. Use a public repository, GitHub Pro/Team, or treat the check as a manual merge criterion.

## What this means in practice

Lockflight makes a careful agent's work verifiable and a careless agent's mistakes visible early. It does not make a hostile
agent safe on your machine. For that, run the agent in an isolated environment with its own identity and let CI with required checks be the gate.
