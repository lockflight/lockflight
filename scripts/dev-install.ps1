<#
.SYNOPSIS
Installs Lockflight on Windows from this checkout, with no package feed and no token.

.DESCRIPTION
The Windows counterpart of scripts/dev-install.sh, with the same layout under the user's home (HOME, else USERPROFILE, as the
engine reads it):
  1. packs the engine for win-x64 into a local feed (~\.lockflight\feed),
  2. installs it as a .NET tool path in ~\.lockflight\tools\<version>, from the Lockflight home, outside any project's
     global.json (an SDK 8 pin cannot restore the RID-specific package),
  3. copies the native engine from that tool path's store to ~\.lockflight\bin\lockflight.exe and records its version in
     ~\.lockflight\bin\lockflight.version,
  4. adds the marketplace and installs the plugin at user scope (needed for sessions started inside a child repo of a workspace
     shell: Claude Code loads project settings only from the directory it starts in).

Why a copy and not a link as on macOS and Linux: creating a symbolic link on Windows needs developer mode or an elevated
session, and the tool path's own lockflight.exe is a small launcher that starts the engine from the store, so it is not one
file that `lockflight link` can copy into a project. A copy of the native engine is one file: it runs from anywhere, `link`
copies it, and the plugin's hook shim runs the project's copy. Windows refuses to overwrite a running program but allows
renaming it, so the old copy is renamed aside first (an update runs from the very engine it replaces) and removed by a later
install once it no longer runs. `lockflight update` keeps the same layout (Updating/UserInstall.cs does what step 3 does).

Run it from PowerShell (Windows PowerShell 5.1 or PowerShell 7):
    powershell -ExecutionPolicy Bypass -File scripts\dev-install.ps1 [-NoPlugin]
or from Git Bash as scripts/dev-install.sh, which runs this script.

Needs the .NET 10 SDK, Visual Studio Build Tools with the "Desktop development with C++" workload (the engine is a Native AOT
build), and Git for Windows: the plugin's hooks run under Git Bash. The plugin step needs `claude` and git access to the
lockflight/lockflight repository.

.PARAMETER NoPlugin
Installs the engine only.

.PARAMETER Updating
Set by `lockflight update`, which prints its own next steps.
#>
[CmdletBinding()]
param([switch]$NoPlugin, [switch]$Updating)

$ErrorActionPreference = "Stop"
$rid = "win-x64"
$binaryName = "lockflight.exe"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$userHome = if ($env:HOME) { $env:HOME } else { $env:USERPROFILE }
$lockflightHome = if ($env:LOCKFLIGHT_HOME) { [IO.Path]::GetFullPath($env:LOCKFLIGHT_HOME) } else { Join-Path $userHome ".lockflight" }

function Fail([string]$message) {
    [Console]::Error.WriteLine("lockflight: $message")
    exit 1
}

# Windows PowerShell turns a native program's redirected stderr into errors, which stop the script under "Stop".
function Invoke-Native([string]$file, [string[]]$arguments, [string]$log) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        if ($log) { & $file @arguments *>> $log } else { & $file @arguments | Out-Host }
        return $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
    }
}

function Read-Native([string]$file, [string[]]$arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        return (& $file @arguments 2>$null) -join "`n"
    } finally {
        $ErrorActionPreference = $previous
    }
}

# The plugin's hooks are a POSIX shell script: Claude Code runs them under Git Bash, as it runs its own shell commands.
function Find-GitBash {
    $candidates = @()
    if ($env:CLAUDE_CODE_GIT_BASH_PATH) { $candidates += $env:CLAUDE_CODE_GIT_BASH_PATH }
    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($git) { $candidates += (Join-Path (Split-Path (Split-Path $git.Source)) "bin\bash.exe") }
    if ($env:ProgramFiles) { $candidates += (Join-Path $env:ProgramFiles "Git\bin\bash.exe") }
    if (${env:ProgramFiles(x86)}) { $candidates += (Join-Path ${env:ProgramFiles(x86)} "Git\bin\bash.exe") }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    return $null
}

foreach ($tool in @("dotnet", "git")) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        Fail "$tool is not on PATH; install the .NET 10 SDK and Git for Windows"
    }
}
$gitBash = Find-GitBash
if (-not $gitBash) {
    Fail "Git Bash was not found (Git for Windows, or CLAUDE_CODE_GIT_BASH_PATH): Lockflight's hooks run under it. Install Git for Windows and run this again"
}

# The version follows the commit, so the same code always gets the same version and a project's pin goes stale only when the
# code changed. A checkout with uncommitted changes gets the time instead. The same numbering as scripts/dev-install.sh.
$changes = Read-Native "git" @("-C", $repo, "status", "--porcelain", "--untracked-files=no")
if (-not $changes) {
    $seconds = [long](Read-Native "git" @("-C", $repo, "log", "-1", "--format=%ct"))
    $stamp = [DateTimeOffset]::FromUnixTimeSeconds($seconds).UtcDateTime.ToString("yyyyMMddHHmmss")
} else {
    $stamp = [DateTime]::UtcNow.ToString("yyyyMMddHHmmss")
}
# A development build is numbered after the last release (the plugin's version) so that it sorts above that release and below
# the next one: 0.2.0-alpha.4.dev.<stamp> after 0.2.0-alpha.4, 0.2.1-0.dev.<stamp> after 0.2.0.
$released = (Get-Content -Raw -LiteralPath (Join-Path $repo "plugins\lockflight\.claude-plugin\plugin.json") | ConvertFrom-Json).version
if ($released -match '^\d+\.\d+\.\d+-') {
    $version = "$released.dev.$stamp"
} elseif ($released -match '^(\d+)\.(\d+)\.(\d+)$') {
    $version = "$($Matches[1]).$($Matches[2]).$([int]$Matches[3] + 1)-0.dev.$stamp"
} else {
    Fail "cannot read the released version from plugins\lockflight\.claude-plugin\plugin.json"
}

$feed = Join-Path $lockflightHome "feed"
$tools = Join-Path $lockflightHome "tools\$version"
$bin = Join-Path $lockflightHome "bin"
New-Item -ItemType Directory -Force -Path $feed, $bin | Out-Null
# `lockflight update` pulls this checkout and runs this script again.
[IO.File]::WriteAllText((Join-Path $lockflightHome "source"), "$repo`n")

$store = Join-Path $tools ".store\lockflight.cli"
if (Test-Path -LiteralPath $store) {
    Write-Output "lockflight: $version is already installed"
} else {
    # The Native AOT linker setup calls vswhere.exe, which the Visual Studio installer does not put on PATH.
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer"
    if ((Test-Path -LiteralPath (Join-Path $vswhere "vswhere.exe")) -and ($env:PATH -notlike "*$vswhere*")) { $env:PATH = "$vswhere;$env:PATH" }

    Write-Output "lockflight: packing $version for $rid"
    $packLog = Join-Path $lockflightHome "pack.log"
    Remove-Item -LiteralPath $packLog -Force -ErrorAction SilentlyContinue
    # From the engine folder, so its global.json picks the SDK. The engine for this runtime, then the package that points the
    # tool at it.
    Push-Location -LiteralPath (Join-Path $repo "engine")
    try {
        foreach ($runtimeSpecific in @($true, $false)) {
            $packArguments = @("pack", "src\Lockflight.Cli", "-c", "Release", "-o", $feed, "-p:Version=$version", "-nologo", "-v", "q")
            if ($runtimeSpecific) { $packArguments += @("-r", $rid) }
            if ((Invoke-Native "dotnet" $packArguments $packLog) -ne 0) {
                Fail "pack failed; see $packLog (a Native AOT build needs Visual Studio Build Tools with the C++ workload)"
            }
        }
    } finally {
        Pop-Location
    }

    Write-Output "lockflight: installing into $tools"
    $installLog = Join-Path $lockflightHome "install.log"
    Remove-Item -LiteralPath $installLog -Force -ErrorAction SilentlyContinue
    # From the Lockflight home, so no project's global.json picks the SDK.
    Push-Location -LiteralPath $lockflightHome
    try {
        $installed = Invoke-Native "dotnet" @("tool", "install", "Lockflight.Cli", "--tool-path", $tools, "--version", $version, "--add-source", $feed) $installLog
    } finally {
        Pop-Location
    }
    if ($installed -ne 0) { Fail "install failed; see $installLog" }
}

$native = @(Get-ChildItem -LiteralPath $store -Recurse -Force -File -Filter $binaryName)
if ($native.Count -ne 1) { Fail "expected one $binaryName in $store, found $($native.Count); delete $tools and run this again" }
$engine = Join-Path $bin $binaryName
$staged = "$engine.new"
Copy-Item -LiteralPath $native[0].FullName -Destination $staged -Force
if (Test-Path -LiteralPath $engine) {
    [IO.File]::Move($engine, "$engine.old-" + [IO.Path]::GetRandomFileName())
}
[IO.File]::Move($staged, $engine)
[IO.File]::WriteAllText((Join-Path $bin "lockflight.version"), "$version`n")
foreach ($aside in @(Get-ChildItem -LiteralPath $bin -Force -File -Filter "$binaryName.old-*")) {
    # One that still runs goes at the next install.
    Remove-Item -LiteralPath $aside.FullName -Force -ErrorAction SilentlyContinue
}
$strayStore = Join-Path $bin ".store"
if (Test-Path -LiteralPath $strayStore) { Remove-Item -LiteralPath $strayStore -Recurse -Force }
$null = Read-Native $engine @("version")
if ($LASTEXITCODE -ne 0) { Fail "the installed engine does not run: $engine" }

if (-not $NoPlugin) {
    if (Get-Command claude -ErrorAction SilentlyContinue) {
        Write-Output "lockflight: installing the plugin at user scope"
        if ((Read-Native "claude" @("plugin", "marketplace", "list")) -notmatch "lockflight") {
            if ((Invoke-Native "claude" @("plugin", "marketplace", "add", "lockflight/lockflight")) -ne 0) { Fail "claude plugin marketplace add lockflight/lockflight failed" }
        }
        $null = Read-Native "claude" @("plugin", "marketplace", "update", "lockflight")
        $plugin = if ((Read-Native "claude" @("plugin", "list")) -match "lockflight@lockflight") { @("plugin", "update", "lockflight@lockflight") } else { @("plugin", "install", "lockflight@lockflight", "--scope", "user") }
        if ((Invoke-Native "claude" $plugin) -ne 0) { Fail "claude $($plugin -join ' ') failed" }
    } else {
        [Console]::Error.WriteLine("lockflight: claude not found; install the plugin later with: claude plugin install lockflight@lockflight --scope user")
    }
}

# lockflight update prints its own next steps.
if ($Updating) { exit 0 }

Write-Output @"

Lockflight $version is installed: $engine
Git Bash, which runs the plugin's hooks: $gitBash
Next, in your project (the workspace shell root, or the repository root), from PowerShell or Git Bash:
  & "$engine" init            # or: init --profile standard
  .lockflight\bin\lockflight.exe doctor
The project's tool pin will name $version, which only this machine's feed has: teammates install it the same way.
A project set up with an earlier version: run & "$engine" update there (it re-pins and links it).
"@
exit 0
