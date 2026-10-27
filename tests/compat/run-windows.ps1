<#
Windows kit: engine build and unit tests, the hook compatibility suite through Git Bash, hook latency, the Claude Code hook
forms, and the user install end to end: scripts/dev-install.ps1, init and doctor in a sample project, Claude Code sessions
where the installed plugin's guard blocks an Edit of lockflight.yaml and a Bash redirect onto it, the Stop gate, the
/lockflight:approve refusal outside an attended session, and `lockflight update`. tests/compat/WINDOWS-CHECK.md lists the
steps, the interactive approval to do by hand afterwards, and what to send back.

Run from the repository root in a normal (non-admin) PowerShell 7 session:
    pwsh -File tests/compat/run-windows.ps1
(PowerShell 7 is required; install with: winget install Microsoft.PowerShell)

Prerequisites: .NET SDK 10, Git for Windows (Git Bash), Claude Code logged in (subscription is fine), and — for the Native AOT
build — Visual Studio Build Tools with the "Desktop development with C++" workload. Without C++ tools the script falls back
to a self-contained single-file build (latency numbers are then marked as non-AOT). -Engine <path> uses a prebuilt engine
instead (e.g. lockflight.exe from the win-x64 release package). -SkipInstall leaves the user install (~\.lockflight and the
plugin at user scope) alone; without it the kit installs from this checkout, as a user would, and updates from it (git pull
--ff-only: run it on main).

Writes everything to tests/compat/results-<machine>-<timestamp>/ — send that folder back (it contains no secrets; review it).
Claude runs use Haiku and cost a few cents in total.
#>
[CmdletBinding()]
param([string]$Model = "haiku", [switch]$SkipClaude, [switch]$SkipInstall, [string]$Engine = "")

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$out = Join-Path $PSScriptRoot ("results-{0}-{1}" -f $env:COMPUTERNAME, (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force $out | Out-Null
$report = Join-Path $out "report.md"
function Log([string]$text) { $text | Tee-Object -FilePath $report -Append }

# ---------------------------------------------------------------- environment
Log "# Windows compatibility report ($(Get-Date -Format o))"
Log "- OS: $([Environment]::OSVersion.VersionString); PowerShell $($PSVersionTable.PSVersion)"
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Log "- Elevated (admin) session: $isAdmin"
#Requires -Version 7
$bashCommand = Get-Command bash -ErrorAction SilentlyContinue
$bash = if ($bashCommand) { $bashCommand.Source } else { "" }
$gitBash = @("$env:ProgramFiles\Git\bin\bash.exe", "${env:ProgramFiles(x86)}\Git\bin\bash.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
Log "- bash on PATH: $bash; Git Bash: $gitBash"
Log "- claude: $(& claude --version 2>&1)"
Log "- dotnet SDKs: $((& dotnet --list-sdks) -join '; ')"
try { $applocker = (Get-AppLockerPolicy -Effective -ErrorAction Stop).RuleCollections.Count; Log "- AppLocker effective rule collections: $applocker" } catch { Log "- AppLocker: not readable ($($_.Exception.Message))" }

# ---------------------------------------------------------------- build + unit tests
Log "`n## build and unit tests"
Push-Location (Join-Path $repo "engine")
$test = & dotnet test 2>&1 | Select-String "total:|failed:|succeeded:"
Log ($test -join "`n")
$bin = Join-Path $out "bin"
if ($Engine) {
    # A prebuilt engine (e.g. from the win-x64 release package) is measured as shipped.
    New-Item -ItemType Directory -Force $bin | Out-Null
    Copy-Item (Resolve-Path $Engine) (Join-Path $bin "lockflight.exe")
    $aot = "prebuilt"
} else {
    # The ILCompiler's linker setup calls vswhere.exe, which the VS installer does not put on PATH.
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
    if ((Test-Path "$vswhere\vswhere.exe") -and ($env:PATH -notlike "*$vswhere*")) { $env:PATH = "$vswhere;$env:PATH" }
    & dotnet publish src/Lockflight.Cli -c Release -r win-x64 -o $bin -nologo -v q 2>&1 | Out-File (Join-Path $out "publish-aot.log")
    $aot = $LASTEXITCODE -eq 0
    if (-not $aot) {
        # The probe suite and the canary copy the engine as one file, so the fallback must be a single file too.
        Log "- Native AOT publish FAILED (see publish-aot.log; usually missing C++ build tools) — falling back to self-contained single-file"
        Remove-Item -Recurse -Force $bin -ErrorAction SilentlyContinue
        & dotnet publish src/Lockflight.Cli -c Release -r win-x64 -o $bin -nologo -v q -p:PublishAot=false --self-contained true -p:PublishSingleFile=true 2>&1 | Out-File (Join-Path $out "publish-fallback.log")
    }
}
Pop-Location
$engine = Join-Path $bin "lockflight.exe"
Log "- engine: $engine (AOT: $aot), version $(& $engine version)"

# ---------------------------------------------------------------- LLM-free probe suite through Git Bash
Log "`n## LLM-free probe suite (Git Bash)"
if ($gitBash) {
    $suite = (Join-Path $repo "tests/compat/probe-suite.sh") -replace '\\', '/'
    $env:ENGINE = $engine -replace '\\', '/'
    $suiteOut = & $gitBash -c "sh '$suite'" 2>&1
    Log ($suiteOut -join "`n")
} else { Log "- skipped: Git Bash not found" }

# ---------------------------------------------------------------- latency
Log "`n## hook latency (200 calls each)"
$proj = Join-Path $out "latency-proj"; New-Item -ItemType Directory -Force $proj | Out-Null
'protected:' + "`n" + '  - "secrets/**"' | Set-Content -NoNewline (Join-Path $proj "lockflight.yaml")
$fixture = (@{ cwd = $proj; tool_name = "Write"; tool_input = @{ file_path = "$proj\src\a.cs" } } | ConvertTo-Json -Compress)
function Measure-Calls([string]$name, [string]$file, [string[]]$arguments) {
    $times = foreach ($i in 1..200) {
        $psi = [Diagnostics.ProcessStartInfo]::new($file); foreach ($a in $arguments) { $psi.ArgumentList.Add($a) }
        $psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.UseShellExecute = $false
        $sw = [Diagnostics.Stopwatch]::StartNew(); $p = [Diagnostics.Process]::Start($psi)
        $p.StandardInput.Write($fixture); $p.StandardInput.Close(); $p.WaitForExit(); $sw.Elapsed.TotalMilliseconds
    }
    $sorted = $times | Sort-Object
    Log ("| {0} | p50 {1:N1} ms | p95 {2:N1} ms |" -f $name, $sorted[99], $sorted[189])
}
Log "| invocation | p50 | p95 |"
Measure-Calls "no-op (cmd /c exit 0)" "cmd.exe" @("/c", "exit 0")
Measure-Calls "engine direct (.exe)" $engine @("hook", "pre-tool-use")
if ($gitBash) {
    $shim = (Join-Path $repo "plugins/lockflight/bin/lockflight-hook") -replace '\\', '/'
    New-Item -ItemType Directory -Force (Join-Path $proj ".lockflight/bin") | Out-Null
    Copy-Item $engine (Join-Path $proj ".lockflight/bin/lockflight.exe")
    Measure-Calls "Git Bash empty (bash -c exit 0)" $gitBash @("-c", "exit 0")
    Measure-Calls "Git Bash shim -> engine (cwd from JSON)" $gitBash @($shim, "pre-tool-use")
    # Claude Code sets CLAUDE_PROJECT_DIR, which lets the shim skip reading the JSON: the path real sessions take.
    $env:CLAUDE_PROJECT_DIR = $proj
    Measure-Calls "Git Bash shim -> engine (CLAUDE_PROJECT_DIR)" $gitBash @($shim, "pre-tool-use")
    Remove-Item Env:CLAUDE_PROJECT_DIR
}
Measure-Calls "powershell -NoProfile (empty)" "powershell.exe" @("-NoProfile", "-Command", "exit 0")

# ---------------------------------------------------------------- real Claude Code hook forms
if (-not $SkipClaude) {
    Log "`n## Claude Code hook forms (claude -p, $Model)"
    Log "| hook form | scenario | onFailure | file created | expected |"
    $prompt = "Use the Write tool (not Bash) to create the file secrets/key.txt with the content hello. If the write is refused, do not retry and do not use any other tool; just reply REFUSED. Otherwise reply DONE."
    function Run-Case([string]$form, [string]$scenario, [string]$onFailure, [hashtable]$hook, [string]$expected) {
        $id = "$form-$scenario-$onFailure"
        $p = Join-Path $out "proj-$id"; $pl = Join-Path $out "plugin-$id"
        New-Item -ItemType Directory -Force $p, "$pl/.claude-plugin", "$pl/hooks" | Out-Null
        'protected:' + "`n" + '  - "secrets/**"' | Set-Content -NoNewline (Join-Path $p "lockflight.yaml")
        '{"name":"lf-win","version":"0.0.0"}' | Set-Content "$pl/.claude-plugin/plugin.json"
        $hook.onFailure = $onFailure; $hook.timeout = 5
        @{ hooks = @{ PreToolUse = @(@{ matcher = "Write|Edit"; hooks = @($hook) }) } } | ConvertTo-Json -Depth 8 | Set-Content "$pl/hooks/hooks.json"
        Push-Location $p
        $null | & claude -p $prompt --plugin-dir $pl --permission-mode acceptEdits --max-turns 4 --model $Model --output-format json > (Join-Path $out "out-$id.json") 2> (Join-Path $out "err-$id.txt")
        Pop-Location
        $created = Test-Path (Join-Path $p "secrets/key.txt")
        Log "| $form | $scenario | $onFailure | $created | $expected |"
    }
    $eng = $engine
    $engNoExt = $engine -replace '\.exe$', ''
    Run-Case "exec .exe"           "engine-ok" "block"    @{ type = "command"; command = $eng; args = @("hook", "pre-tool-use") } "False"
    Run-Case "exec no-extension"   "engine-ok" "block"    @{ type = "command"; command = $engNoExt; args = @("hook", "pre-tool-use") } "False (if resolved)"
    Run-Case "shell (default)"     "engine-ok" "block"    @{ type = "command"; command = "`"$eng`" hook pre-tool-use" } "False"
    Run-Case "shell powershell"    "engine-ok" "block"    @{ type = "command"; shell = "powershell"; command = "& '$eng' hook pre-tool-use; exit `$LASTEXITCODE" } "False"
    Run-Case "exec missing .exe"   "missing"   "block"    @{ type = "command"; command = "C:\does-not-exist\lockflight.exe"; args = @("hook", "pre-tool-use") } "False"
    Run-Case "exec missing .exe"   "missing"   "continue" @{ type = "command"; command = "C:\does-not-exist\lockflight.exe"; args = @("hook", "pre-tool-use") } "True"
}

# ---------------------------------------------------------------- user install, project, sessions, update
if (-not $SkipInstall) {
    Log "`n## user install (scripts/dev-install.ps1)"
    $installLog = Join-Path $out "install.txt"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo "scripts\dev-install.ps1") *> $installLog
    Log "- dev-install.ps1 exit: $LASTEXITCODE (output in install.txt)"
    $userHome = if ($env:HOME) { $env:HOME } else { $env:USERPROFILE }
    $installed = Join-Path $userHome ".lockflight\bin\lockflight.exe"
    $item = Get-Item -LiteralPath $installed -ErrorAction SilentlyContinue
    Log "- engine: $installed exists: $([bool]$item); link type: '$($item.LinkType)' (expected empty: a copy)"
    Log "- version: $(& $installed version 2>&1); recorded: $(Get-Content -Raw -LiteralPath (Join-Path $userHome '.lockflight\bin\lockflight.version') -ErrorAction SilentlyContinue)"
    Log "- plugin at user scope: $((& claude plugin list 2>&1 | Select-String 'lockflight@lockflight') -join '; ')"

    Log "`n## project: init and doctor (a copy of samples/dotnet-single, in a folder with a space)"
    $session = Join-Path $out "session proj"
    Copy-Item -Recurse (Join-Path $repo "samples\dotnet-single") $session
    $feature = "specs\20261011-120000-shipping"
    New-Item -ItemType Directory -Force (Join-Path $session $feature) | Out-Null
    @"
# Feature Specification: Shipping cost

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Pay for shipping (Priority: P1)

1. **Given** a 2.1 kg parcel and a 10.00 order, **When** shipping is priced, **Then** it costs 10.00

## Requirements *(mandatory)*

- **FR-001**: System MUST charge 5.00 for the first kilogram
"@ | Set-Content -LiteralPath (Join-Path $session "$feature\spec.md")
    Push-Location -LiteralPath $session
    & git init --quiet; & git add -A; & git -c user.email=kit@example.com -c user.name=kit commit --quiet -m base
    & $installed init *> (Join-Path $out "init.txt")
    Log "- init exit: $LASTEXITCODE (output in init.txt)"
    & git add -A; & git -c user.email=kit@example.com -c user.name=kit commit --quiet -m lockflight
    & .lockflight\bin\lockflight.exe doctor *> (Join-Path $out "doctor.txt")
    Log "- doctor exit: $LASTEXITCODE (output in doctor.txt)"
    Pop-Location
    $yaml = Join-Path $session "lockflight.yaml"
    $yamlBefore = Get-Content -Raw -LiteralPath $yaml
    $ledger = Join-Path $session ".lockflight\ledger"
    function Ledger-Count([string]$text) {
        @(Get-ChildItem -LiteralPath $ledger -Filter *.jsonl -ErrorAction SilentlyContinue | Select-String -SimpleMatch $text).Count
    }

    if (-not $SkipClaude) {
        Log "`n## Claude Code sessions with the installed plugin (claude -p, $Model)"
        Log "| scenario | result | expected |"
        function Run-Session([string]$id, [string]$prompt, [string[]]$tools) {
            Push-Location -LiteralPath $session
            $null | & claude -p $prompt --allowedTools ($tools -join ",") --max-turns 6 --model $Model --output-format json > (Join-Path $out "session-$id.json") 2> (Join-Path $out "session-$id-err.txt")
            Pop-Location
        }
        $blockedBefore = Ledger-Count '"hook.blocked"'
        Run-Session "edit-manifest" "Use the Edit tool (not Bash) to replace the text 'version: 1' with 'version: 2' in lockflight.yaml. If the edit is refused, do not retry and do not use any other tool; reply REFUSED. Otherwise reply DONE." @("Edit", "Read")
        Log "| guard: Edit lockflight.yaml | unchanged: $((Get-Content -Raw -LiteralPath $yaml) -eq $yamlBefore); blocks logged: $((Ledger-Count '"hook.blocked"') - $blockedBefore) | unchanged: True; blocks >= 1 |"
        $blockedBefore = Ledger-Count '"hook.blocked"'
        Run-Session "bash-redirect" "Use the Bash tool to run exactly this command: echo changed > lockflight.yaml . If it is refused, do not retry and do not use any other tool; reply REFUSED. Otherwise reply DONE." @("Bash")
        Log "| guard: Bash redirect onto lockflight.yaml | unchanged: $((Get-Content -Raw -LiteralPath $yaml) -eq $yamlBefore); blocks logged: $((Ledger-Count '"hook.blocked"') - $blockedBefore) | unchanged: True; blocks >= 1 |"
        $stopBefore = Ledger-Count '"stop-hook"'
        Run-Session "stop-gate" "Use the Write tool to create src/Shop.Api/Broken.cs containing exactly: public class Broken { public int X => ; } Then reply DONE. If you are told the build fails, fix the file so it compiles and reply FIXED." @("Write", "Edit", "Read")
        Log "| Stop gate: a turn ending with a compile error | gate runs from the Stop hook: $((Ledger-Count '"stop-hook"') - $stopBefore); session-stop-gate.json says: $((Get-Content -Raw (Join-Path $out 'session-stop-gate.json') -ErrorAction SilentlyContinue | ConvertFrom-Json -ErrorAction SilentlyContinue).result) | runs >= 2 (one failing, one after the fix); FIXED |"
        Run-Session "approve-unattended" "/lockflight:approve spec" @("Read")
        Push-Location -LiteralPath $session
        & .lockflight\bin\lockflight.exe approve verify --feature 20261011-120000-shipping --require spec *> (Join-Path $out "approve-verify-unattended.txt")
        $verify = $LASTEXITCODE
        Pop-Location
        Log "| /lockflight:approve spec in claude -p (not attended) | approve verify exit: $verify; stderr mentions it: $([bool](Select-String -Quiet -SimpleMatch -Pattern 'nothing was approved' -Path (Join-Path $out 'session-approve-unattended-err.txt'), (Join-Path $out 'session-approve-unattended.json') -ErrorAction SilentlyContinue)) | exit not 0 (nothing approved) |"
    }

    Log "`n## lockflight update (from this checkout, in the project)"
    Push-Location -LiteralPath $session
    & $installed update *> (Join-Path $out "update.txt")
    Log "- update exit: $LASTEXITCODE (output in update.txt)"
    Log "- installed engine version after update: $(& $installed version 2>&1); project engine: $(& .lockflight\bin\lockflight.exe version 2>&1)"
    Pop-Location
    Log "- engines set aside and not yet removed: $(@(Get-ChildItem -LiteralPath (Split-Path $installed) -Filter 'lockflight.exe.old-*' -ErrorAction SilentlyContinue).Count) (one is normal: the update ran from it)"
    Log "- for the interactive approval, open Claude Code in: $session (see WINDOWS-CHECK.md)"
}

Log "`nDone. Results folder: $out"
