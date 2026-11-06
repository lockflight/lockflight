<#
.SYNOPSIS
  Lockflight engine install for Windows (win-x64): a person's install on a machine, from the released package. No .NET SDK
  and no build tools needed.

.DESCRIPTION
  Downloads the engine's win-x64 package from nuget.org or the feed in LOCKFLIGHT_NUGET_FEED (a flat-container base or a v3
  index.json; https:// only, file:// for tests), extracts lockflight.exe and checks its SHA-256 against a lock, or, without
  one, against the version's engine-hashes.json in the pointer package of the same feed. Nothing runs the engine before the
  check passes. LOCKFLIGHT_NUGET_USER and LOCKFLIGHT_NUGET_TOKEN give basic auth for the feed; LOCKFLIGHT_RELEASE_FALLBACK=1
  downloads the bare executable from the GitHub Release instead.

  Without -Dir it completes the user install, the layout the engine, the plugin's hook shim and `lockflight link --installed`
  read under the home folder (HOME, else USERPROFILE, as the engine reads it):
    ~\.lockflight\tools\<version>\lockflight.exe   the checked engine
    ~\.lockflight\bin\lockflight.exe              a copy of it, the user engine (Windows has no symbolic link without
                                                  developer mode), with its version in bin\lockflight.version
  and puts ~\.lockflight\bin on the user PATH (the per-user variable, never the system one) once, unless -NoModifyPath.
  An install by -Version points bin at that version; an install from a lock does so only when bin holds no engine yet, and
  otherwise leaves the user engine as it is and names the command that links the locked engine into the project. A second run
  that finds everything in place changes nothing. With -Dir it only writes the executable into that folder.

  The last line of output is the installed executable's path (in tools\<version>, or -Dir). Exit 0 installed, 1 failed,
  4 usage. Works in Windows PowerShell 5.1 and PowerShell 7.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File install.ps1 -Version 0.3.1
#>
param(
  [string]$Lock = "",
  [string]$Version = "",
  [string]$Dir = "",
  [switch]$NoModifyPath
)

$ErrorActionPreference = "Stop"
# Windows PowerShell 5.1 defaults to older TLS versions, which nuget.org and GitHub refuse.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.IO.Compression.FileSystem

$Rid = "win-x64"
$DefaultFeed = "https://api.nuget.org/v3-flatcontainer/"
$ReleaseBase = "https://github.com/lockflight/lockflight/releases/download"

function Say([string]$Message) { [Console]::Error.WriteLine("lockflight install: $Message") }
function Fail([string]$Message) { Say $Message; exit 1 }
function Usage([string]$Message) { Say $Message; Say "usage: install.ps1 [-Lock <engine.lock>] [-Version <v>] [-Dir <dir>] [-NoModifyPath]"; exit 4 }

function Get-Url([string]$Url, [string]$OutFile, [bool]$WithCredentials) {
  if (-not ($Url.StartsWith("https://") -or $Url.StartsWith("file://"))) { Fail "refusing to download ${Url}: only https:// feeds (and file:// for tests)" }
  if ($Url.StartsWith("file://")) {
    $source = ([Uri]$Url).LocalPath
    if (-not (Test-Path -LiteralPath $source)) { throw "not found: $Url" }
    Copy-Item -LiteralPath $source -Destination $OutFile
    return
  }
  $headers = @{}
  if ($WithCredentials -and $env:LOCKFLIGHT_NUGET_USER -and $env:LOCKFLIGHT_NUGET_TOKEN) {
    $pair = [Text.Encoding]::UTF8.GetBytes("$($env:LOCKFLIGHT_NUGET_USER):$($env:LOCKFLIGHT_NUGET_TOKEN)")
    $headers["Authorization"] = "Basic " + [Convert]::ToBase64String($pair)
  }
  Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $OutFile -Headers $headers -MaximumRedirection 5
}

function Get-FeedBase {
  $feed = if ($env:LOCKFLIGHT_NUGET_FEED) { $env:LOCKFLIGHT_NUGET_FEED } else { $DefaultFeed }
  if ($feed.EndsWith("/index.json")) {
    $index = Join-Path $script:Work "index.json"
    try { Get-Url $feed $index $true } catch { Fail "could not read the service index ${feed}: $_" }
    $resource = (Get-Content -Raw -LiteralPath $index | ConvertFrom-Json).resources | Where-Object { $_."@type" -eq "PackageBaseAddress/3.0.0" } | Select-Object -First 1
    if (-not $resource) { Fail "the service index $feed has no PackageBaseAddress/3.0.0" }
    $feed = $resource."@id"
  }
  if (-not $feed.EndsWith("/")) { $feed += "/" }
  return $feed
}

function Read-Entry([string]$Package, [string]$Pattern, [string]$OutFile) {
  $archive = [IO.Compression.ZipFile]::OpenRead($Package)
  try {
    $matching = @($archive.Entries | Where-Object { $_.FullName -match $Pattern })
    if ($matching.Count -ne 1) { Fail "$Package holds $($matching.Count) entries matching $Pattern, expected one" }
    $entry = $matching[0]
    if ($entry.FullName.StartsWith("/") -or $entry.FullName.Split("/") -contains "..") { Fail "${Package}: refusing the entry $($entry.FullName)" }
    $stream = $entry.Open()
    try {
      $file = [IO.File]::Create($OutFile)
      try { $stream.CopyTo($file) } finally { $file.Dispose() }
    } finally { $stream.Dispose() }
  } finally { $archive.Dispose() }
}

function Get-Sha256([string]$Path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant() }

# The user engine in bin: a staged copy of the checked engine, checked again, then renamed into place. Windows refuses to
# overwrite a running program but allows renaming it, so an engine that runs is renamed aside and removed by a later install.
function Install-UserEngine([string]$Source, [string]$Bin, [string]$Version, [string]$Expected) {
  New-Item -ItemType Directory -Force -Path $Bin | Out-Null
  $engine = Join-Path $Bin "lockflight.exe"
  $staged = Join-Path $Bin ".lockflight.$PID.exe"
  Copy-Item -LiteralPath $Source -Destination $staged -Force
  if ((Get-Sha256 $staged) -ne $Expected) {
    Remove-Item -LiteralPath $staged -Force
    Fail "the copy in $Bin is not the checked engine (deleted)"
  }
  if (Test-Path -LiteralPath $engine) { [IO.File]::Move($engine, "$engine.old-" + [IO.Path]::GetRandomFileName()) }
  [IO.File]::Move($staged, $engine)
  [IO.File]::WriteAllText((Join-Path $Bin "lockflight.version"), "$Version`n")
  foreach ($aside in @(Get-ChildItem -LiteralPath $Bin -Force -File -Filter "lockflight.exe.old-*")) {
    Remove-Item -LiteralPath $aside.FullName -Force -ErrorAction SilentlyContinue
  }
  # `dotnet tool install --tool-path` into bin leaves a launcher (lockflight.cmd) that starts the engine from a store beside it;
  # the engine and the hook shim run bin\lockflight.exe, so both go, as `lockflight update` removes that store.
  $launcher = Join-Path $Bin "lockflight.cmd"
  $store = Join-Path $Bin ".store"
  if (Test-Path -LiteralPath $launcher) { Remove-Item -LiteralPath $launcher -Force }
  if (Test-Path -LiteralPath $store) { Remove-Item -LiteralPath $store -Recurse -Force }
}

# The user PATH lives in the registry as an expandable string: read and written raw, so entries such as %USERPROFILE%\bin stay
# as they are. Then Windows is told the environment changed, so terminals opened from now on see it.
function Add-UserPath([string]$Folder) {
  if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    Say "not Windows: the user PATH is left alone"
    return
  }
  $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("Environment")
  try {
    $current = [string]$key.GetValue("Path", "", [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
    $entries = @($current -split ";" | Where-Object { $_ })
    $wanted = $Folder.TrimEnd([char]92)
    foreach ($entry in $entries) {
      if ([Environment]::ExpandEnvironmentVariables($entry).TrimEnd([char]92) -ieq $wanted) {
        Say "$Folder is already on your user PATH"
        return
      }
    }
    $key.SetValue("Path", (($entries + $Folder) -join ";"), [Microsoft.Win32.RegistryValueKind]::ExpandString)
  } finally {
    $key.Close()
  }
  try {
    Add-Type -Namespace Lockflight -Name Environment -MemberDefinition @"
[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);
"@
    $result = [UIntPtr]::Zero
    [void][Lockflight.Environment]::SendMessageTimeout([IntPtr]0xffff, 0x1A, [UIntPtr]::Zero, "Environment", 2, 5000, [ref]$result)
  } catch {
    Say "sign out and in again if new terminals do not find lockflight"
  }
  Say "added $Folder to your user PATH (HKEY_CURRENT_USER\Environment, Path). Open a new terminal to run lockflight by name;"
  Say "  an app that was already open, such as VS Code, passes the old PATH to its terminals until you restart it."
  Say "  To undo: Environment Variables, User variables, Path: remove $Folder"
}

if (-not $Lock -and -not $Version -and (Test-Path -LiteralPath ".lockflight/engine.lock")) { $Lock = ".lockflight/engine.lock" }
if (-not $Lock -and -not $Version) { Usage "give -Lock <engine.lock>, or -Version <v> for a first install without a lock" }
$fromLock = [bool]$Lock
$userInstall = -not $Dir
$userHome = if ($env:HOME) { $env:HOME } elseif ($env:USERPROFILE) { $env:USERPROFILE } else { $HOME }
$lockflightHome = Join-Path $userHome ".lockflight"

$script:Work = Join-Path ([IO.Path]::GetTempPath()) ("lockflight-install-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $script:Work | Out-Null
try {
  $expected = ""
  if ($Lock) {
    if (-not (Test-Path -LiteralPath $Lock)) { Fail "no lock at $Lock" }
    $text = Get-Content -Raw -LiteralPath $Lock
    # install.sh reads a lock without decoding JSON escapes, so every reader refuses a lock with a backslash.
    if ($text.Contains("\")) { Fail "$Lock holds a backslash; a lock names its keys and values without escapes" }
    $document = $text | ConvertFrom-Json
    if ($document.schema -ne "lockflight.engine-lock/v1") { Fail "$Lock is not a lockflight.engine-lock/v1 file" }
    if ($document.channel -ne "release") { Fail "$Lock is a '$($document.channel)' lock (a local development build); only release locks are installed" }
    if ($Version -and $Version -ne $document.version) { Fail "-Version $Version differs from the lock's $($document.version)" }
    $Version = $document.version
    $expected = $document.binaries.$Rid.sha256
    if (-not $expected) { Fail "$Lock has no hash for $Rid" }
  }
  if ($Version -notmatch '^[0-9A-Za-z.-]+$') { Fail "'$Version' is not an engine version" }
  $lower = $Version.ToLowerInvariant()
  if (-not $Dir) { $Dir = Join-Path (Join-Path $lockflightHome "tools") $Version }
  New-Item -ItemType Directory -Force -Path $Dir | Out-Null

  if (-not $expected) {
    $base = Get-FeedBase
    $pointer = Join-Path $script:Work "pointer.nupkg"
    try { Get-Url "${base}lockflight.cli/$lower/lockflight.cli.$lower.nupkg" $pointer $true } catch { Fail "could not download Lockflight.Cli $Version from ${base}: $_" }
    $hashesFile = Join-Path $script:Work "engine-hashes.json"
    Read-Entry $pointer '^engine-hashes\.json$' $hashesFile
    $hashes = Get-Content -Raw -LiteralPath $hashesFile | ConvertFrom-Json
    if ($hashes.schema -ne "lockflight.engine-hashes/v1" -or $hashes.version -ne $Version) { Fail "engine-hashes.json of $Version does not describe Lockflight $Version" }
    $expected = $hashes.binaries.$Rid.sha256
    if (-not $expected) { Fail "engine-hashes.json of $Version has no hash for $Rid" }
  }
  if ($expected -cnotmatch '^[0-9a-f]{64}$') { Fail "the expected hash '$expected' is not a lower-case SHA-256" }

  $target = Join-Path $Dir "lockflight.exe"
  if ((Test-Path -LiteralPath $target) -and (Get-Sha256 $target) -eq $expected) {
    Say "Lockflight $Version ($Rid, sha256 $expected) is already installed in $Dir"
  } else {
    $candidate = Join-Path $script:Work "lockflight.exe"
    if ($env:LOCKFLIGHT_RELEASE_FALLBACK -eq "1") {
      try { Get-Url "$ReleaseBase/v$Version/lockflight-$Version-$Rid.exe" $candidate $false } catch { Fail "could not download lockflight-$Version-$Rid.exe from the GitHub Release: $_" }
    } else {
      $base = Get-FeedBase
      $package = Join-Path $script:Work "lockflight.cli.$Rid.nupkg"
      try { Get-Url "${base}lockflight.cli.$Rid/$lower/lockflight.cli.$Rid.$lower.nupkg" $package $true } catch { Fail "could not download Lockflight.Cli.$Rid $Version from ${base}: $_" }
      Read-Entry $package "^tools/[^/]+/$Rid/lockflight\.exe$" $candidate
    }

    # Verify before anything runs it: the script never executes the engine.
    $actual = Get-Sha256 $candidate
    if ($actual -ne $expected) {
      Remove-Item -LiteralPath $candidate -Force
      Fail "the downloaded engine is not the locked one: expected $expected, got $actual (deleted)"
    }
    $staged = Join-Path $Dir ".lockflight.$PID.exe"
    Copy-Item -LiteralPath $candidate -Destination $staged -Force
    Move-Item -LiteralPath $staged -Destination $target -Force
    Say "installed Lockflight $Version ($Rid, sha256 $actual)"
  }

  if ($userInstall) {
    $bin = Join-Path $lockflightHome "bin"
    $userEngine = Join-Path $bin "lockflight.exe"
    $recorded = Join-Path $bin "lockflight.version"
    $current = if (Test-Path -LiteralPath $recorded) { (Get-Content -Raw -LiteralPath $recorded).Trim() } else { "" }
    $hasEngine = Test-Path -LiteralPath $userEngine
    if ($hasEngine -and $current -eq $Version -and (Get-Sha256 $userEngine) -eq $expected) {
      Say "the user engine $userEngine is Lockflight $Version already"
    } elseif ($hasEngine -and $fromLock) {
      $held = if ($current) { "Lockflight $current" } else { "another engine" }
      Say "the user engine $userEngine stays $held; in the project, link the locked $Version with: & `"$userEngine`" link --installed"
    } else {
      Install-UserEngine $target $bin $Version $expected
      Say "the user engine is now Lockflight $Version`: $userEngine"
      # An install from source recorded its checkout, which `lockflight update` would rebuild from; this is a package install now.
      $source = Join-Path $lockflightHome "source"
      if (Test-Path -LiteralPath $source) {
        Remove-Item -LiteralPath $source -Force
        Say "removed ${source}: lockflight update now installs the newest package instead of rebuilding a checkout"
      }
    }
    if (-not $NoModifyPath) { Add-UserPath $bin }

    [Console]::Error.WriteLine(@"

Next, install the plugin (Claude Code runs its hooks under Git Bash, from Git for Windows):
  claude plugin marketplace add lockflight/lockflight
  claude plugin install lockflight@lockflight --scope user
Then, at your project's root, from PowerShell:
  & "$userEngine" init
  .lockflight\bin\lockflight.exe doctor
A project that pins Lockflight already: & "$userEngine" engine install (the locked engine) or update (the newest).
"@)
  }
  Write-Output $target
} finally {
  Remove-Item -LiteralPath $script:Work -Recurse -Force -ErrorAction SilentlyContinue
}
