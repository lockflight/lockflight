<#
Windows kit: engine build and unit tests, the hook compatibility suite through Git Bash, hook latency, and the Claude Code
hook forms.

Run from the repository root in a normal (non-admin) PowerShell 7 session:
    pwsh -File tests/compat/run-windows.ps1
(PowerShell 7 is required; install with: winget install Microsoft.PowerShell)

Prerequisites: .NET SDK 10, Git for Windows (Git Bash), Claude Code logged in (subscription is fine), and — for the Native AOT
build — Visual Studio Build Tools with the "Desktop development with C++" workload. Without C++ tools the script falls back
to a self-contained single-file build (latency numbers are then marked as non-AOT). -Engine <path> uses a prebuilt engine
instead (e.g. lockflight.exe from the win-x64 release package).

Writes everything to tests/compat/results-<machine>-<timestamp>/ — send that folder back (it contains no secrets; review it).
Claude runs use Haiku and cost a few cents in total.
#>
[CmdletBinding()]
param([string]$Model = "haiku", [switch]$SkipClaude, [string]$Engine = "")

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

Log "`nDone. Results folder: $out"
