<#
.SYNOPSIS
  Lockflight engine install for Windows (win-x64): a person's first install on a machine. No .NET SDK needed.

.DESCRIPTION
  Downloads the engine's win-x64 package from nuget.org or the feed in LOCKFLIGHT_NUGET_FEED (a flat-container base or a v3
  index.json; https:// only, file:// for tests), extracts lockflight.exe and checks its SHA-256 against a lock, or, without
  one, against the version's engine-hashes.json in the pointer package of the same feed. Nothing runs the engine before the
  check passes. LOCKFLIGHT_NUGET_USER and LOCKFLIGHT_NUGET_TOKEN give basic auth for the feed; LOCKFLIGHT_RELEASE_FALLBACK=1
  downloads the bare executable from the GitHub Release instead. The last line of output is the installed path.
  Exit 0 installed, 1 failed, 4 usage. Works in Windows PowerShell 5.1 and PowerShell 7.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File install.ps1 -Version 0.3.0
#>
param(
  [string]$Lock = "",
  [string]$Version = "",
  [string]$Dir = ""
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
function Usage([string]$Message) { Say $Message; Say "usage: install.ps1 [-Lock <engine.lock>] [-Version <v>] [-Dir <dir>]"; exit 4 }

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

if (-not $Lock -and -not $Version -and (Test-Path -LiteralPath ".lockflight/engine.lock")) { $Lock = ".lockflight/engine.lock" }
if (-not $Lock -and -not $Version) { Usage "give -Lock <engine.lock>, or -Version <v> for a first install without a lock" }

$script:Work = Join-Path ([IO.Path]::GetTempPath()) ("lockflight-install-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $script:Work | Out-Null
try {
  $expected = ""
  if ($Lock) {
    if (-not (Test-Path -LiteralPath $Lock)) { Fail "no lock at $Lock" }
    $document = Get-Content -Raw -LiteralPath $Lock | ConvertFrom-Json
    if ($document.schema -ne "lockflight.engine-lock/v1") { Fail "$Lock is not a lockflight.engine-lock/v1 file" }
    if ($document.channel -ne "release") { Fail "$Lock is a '$($document.channel)' lock (a local development build); only release locks are installed" }
    if ($Version -and $Version -ne $document.version) { Fail "-Version $Version differs from the lock's $($document.version)" }
    $Version = $document.version
    $expected = $document.binaries.$Rid.sha256
    if (-not $expected) { Fail "$Lock has no hash for $Rid" }
  }
  if ($Version -notmatch '^[0-9A-Za-z.-]+$') { Fail "'$Version' is not an engine version" }
  $lower = $Version.ToLowerInvariant()
  if (-not $Dir) { $Dir = Join-Path (Join-Path (Join-Path $HOME ".lockflight") "tools") $Version }
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
  $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $candidate).Hash.ToLowerInvariant()
  if ($actual -ne $expected) {
    Remove-Item -LiteralPath $candidate -Force
    Fail "the downloaded engine is not the locked one: expected $expected, got $actual (deleted)"
  }
  $target = Join-Path $Dir "lockflight.exe"
  $staged = Join-Path $Dir ".lockflight.$PID.exe"
  Copy-Item -LiteralPath $candidate -Destination $staged -Force
  Move-Item -LiteralPath $staged -Destination $target -Force
  Say "installed Lockflight $Version ($Rid, sha256 $actual)"
  Write-Output $target
} finally {
  Remove-Item -LiteralPath $script:Work -Recurse -Force -ErrorAction SilentlyContinue
}
