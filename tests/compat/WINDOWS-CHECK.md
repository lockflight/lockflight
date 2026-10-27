# Windows check

A run on a real Windows machine with Claude Code, to confirm what CI cannot: the installed plugin's hooks in real sessions,
the interactive approval, and `update` on a machine people use. It takes about 30 minutes, most of it builds. Claude runs use
Haiku and cost a few cents.

## Before you start

- Windows 10 or 11, x64. A normal (not elevated) terminal.
- [PowerShell 7](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows)
  (`winget install Microsoft.PowerShell`), the [.NET 10 SDK](https://dotnet.microsoft.com/download),
  [Git for Windows](https://git-scm.com/download/win), and Visual Studio Build Tools with the *Desktop development with C++*
  workload (`winget install Microsoft.VisualStudio.2022.BuildTools`, then add the workload in the Visual Studio Installer).
- Claude Code installed and logged in (`claude --version` prints 2.1.295 or newer).
- The kit installs Lockflight for your user (`%USERPROFILE%\.lockflight`, the plugin at user scope) and replaces an earlier
  Lockflight install there. Add `-SkipInstall` to leave it alone (then only the first sections run).

## Steps

1. Clone into a folder whose path has a space, and stay on `main` (`update` pulls it):

   ```powershell
   git clone https://github.com/lockflight/lockflight "$env:USERPROFILE\src\lockflight check"
   cd "$env:USERPROFILE\src\lockflight check"
   ```

2. Run the kit from that folder:

   ```powershell
   pwsh -File tests\compat\run-windows.ps1
   ```

   It prints the results folder at the end: `tests\compat\results-<machine>-<time>\`.

3. The interactive approval (a session typed by you, which `claude -p` cannot be). In a **new** terminal:

   ```powershell
   cd "<results folder>\session proj"
   claude
   ```

   - Type `/lockflight:approve spec`. Expected: the engine shows the spec it would approve and a one-time code.
   - Type `/lockflight:approve spec <code>` with that code. Expected: the approval is recorded.
   - Ask: *"Edit lockflight.yaml and change version: 1 to version: 2"*. Expected: the edit is refused with a Lockflight
     message.
   - Exit Claude Code (`/exit`), then in the same terminal:

     ```powershell
     .lockflight\bin\lockflight.exe approve verify --feature 20261011-120000-shipping --require spec
     ```

     Expected: exit 0.

   Write what you saw into `manual.txt` in the results folder: the messages, in a few lines, and the output of `approve verify`.

4. From Git Bash, once: `cd "<checkout>"` and `scripts/dev-install.sh --no-plugin`. Expected: `already installed`, then the
   engine path, and `already on your user PATH`. In a new terminal, `lockflight version` works by name. Add the output to
   `manual.txt`.

## What to send back

The results folder, zipped. Read it first: it holds no secrets, but the paths include your user name, and the session logs
include what Claude replied.

`report.md` in it should show:

| Section | Expected |
|---|---|
| build and unit tests | `failed: 0` |
| LLM-free probe suite | no `FAIL` lines |
| user install | `dev-install.ps1 exit: 0`, `link type: ''`, the version twice, one user PATH entry, the plugin listed |
| project | `init exit: 0`; `doctor exit: 0` (doctor.txt lists any warning) |
| guard: Edit lockflight.yaml | `unchanged: True`, at least one block logged |
| guard: Bash redirect | `unchanged: True`, at least one block logged |
| Stop gate | at least two gate runs from the Stop hook, the reply `FIXED` |
| `/lockflight:approve` in `claude -p` | `approve verify` exit not 0: nothing is approved outside an attended session |
| `lockflight update` | `update exit: 0`, the installed and project engine versions equal |

Anything else, or a section that did not run, is worth a line in `manual.txt`.
