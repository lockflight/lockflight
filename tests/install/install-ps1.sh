#!/usr/bin/env bash
# A smoke test of scripts/install.ps1, the Windows install a person runs once (a Release asset; no CI template uses it), with
# PowerShell 7 against a local file:// feed: a lock installs the win-x64 executable, a first install by version takes the hash
# from engine-hashes.json and completes the user install (~/.lockflight/bin/lockflight.exe and its version), a second run
# changes nothing, a lock leaves a user engine of another version alone, an install by version replaces it and the launcher
# of a `dotnet tool install` into bin, and a hash mismatch installs nothing. Skipped where pwsh is missing. Windows PowerShell
# 5.1, the real engine and the user PATH are run by CI's windows-install job.
set -euo pipefail

# In CI a missing pwsh fails the suite: a runner image without it would otherwise pass having tested nothing.
if ! command -v pwsh >/dev/null; then
  if [ -n "${CI:-}" ]; then echo "FAIL pwsh is not installed, so install.ps1 was not tested"; exit 1; fi
  echo "skipped: no pwsh"; exit 0
fi
root=$(cd "$(dirname "$0")/../.." && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
version=0.0.0-ci.7
rid=win-x64
failures=0
ok() { echo "ok   $1"; }
bad() { echo "FAIL $1"; failures=$((failures + 1)); }
sha256() { if command -v sha256sum >/dev/null; then sha256sum "$1" | cut -d ' ' -f 1; else shasum -a 256 "$1" | cut -d ' ' -f 1; fi; }

printf 'MZ stand-in engine\n' > "$work/lockflight.exe"
hash=$(sha256 "$work/lockflight.exe")
feed="$work/feed"
mkdir -p "$feed/lockflight.cli.$rid/$version" "$feed/lockflight.cli/$version"
printf '{\n  "schema": "lockflight.engine-hashes/v1",\n  "package": "Lockflight.Cli",\n  "version": "%s",\n  "commit": "%s",\n  "binaries": {\n    "%s": { "sha256": "%s", "path": "tools/any/%s/lockflight.exe" }\n  }\n}\n' \
  "$version" "$(printf '1%.0s' $(seq 40))" "$rid" "$hash" "$rid" > "$work/engine-hashes.json"
lock() { printf '{\n  "schema": "lockflight.engine-lock/v1",\n  "package": "Lockflight.Cli",\n  "version": "%s",\n  "channel": "release",\n  "binaries": {\n    "%s": { "sha256": "%s" }\n  }\n}\n' "$version" "$rid" "$1"; }
lock "$hash" > "$work/engine.lock"
lock "$(printf 'b%.0s' $(seq 64))" > "$work/wrong.lock"
pack() { # pack <nupkg> <entry> <file>
  mkdir -p "$(dirname "$1")"
  if command -v python3 >/dev/null; then
    python3 -I -c 'import sys, zipfile; zipfile.ZipFile(sys.argv[1], "w", zipfile.ZIP_DEFLATED).write(sys.argv[3], sys.argv[2])' "$1" "$2" "$3"
  else
    perl -MIO::Compress::Zip=zip,\$ZipError -e 'zip($ARGV[2] => $ARGV[0], Name => $ARGV[1]) or die $ZipError' "$1" "$2" "$3"
  fi
}
pack "$feed/lockflight.cli.$rid/$version/lockflight.cli.$rid.$version.nupkg" "tools/any/$rid/lockflight.exe" "$work/lockflight.exe"
pack "$feed/lockflight.cli/$version/lockflight.cli.$version.nupkg" engine-hashes.json "$work/engine-hashes.json"
# A second release, for a lock that names another version than the user engine, and for an upgrade by version.
version2=0.0.0-ci.8
mkdir -p "$work/second"
printf 'MZ stand-in engine 2\n' > "$work/second/lockflight.exe"
hash2=$(sha256 "$work/second/lockflight.exe")
sed -e "s/$version/$version2/" -e "s/$hash/$hash2/" "$work/engine-hashes.json" > "$work/second/engine-hashes.json"
pack "$feed/lockflight.cli.$rid/$version2/lockflight.cli.$rid.$version2.nupkg" "tools/any/$rid/lockflight.exe" "$work/second/lockflight.exe"
pack "$feed/lockflight.cli/$version2/lockflight.cli.$version2.nupkg" engine-hashes.json "$work/second/engine-hashes.json"
sed -e "s/$version/$version2/" "$work/engine.lock" | sed -e "s/$hash/$hash2/" > "$work/second.lock"

install() { LOCKFLIGHT_NUGET_FEED="file://$feed/" HOME="$work/profile" pwsh -NoProfile -File "$root/scripts/install.ps1" "$@"; }

if out=$(install -Lock "$work/engine.lock" -Dir "$work/out" 2> "$work/err") && [ "$(printf '%s\n' "$out" | tail -n 1)" = "$work/out/lockflight.exe" ] \
  && [ "$(sha256 "$work/out/lockflight.exe")" = "$hash" ]; then
  ok "a lock installs the locked win-x64 engine and prints its path last"
else
  bad "lock install: $out $(cat "$work/err")"
fi
[ ! -e "$work/profile/.lockflight/bin" ] && ok "an install into -Dir leaves the user install alone" || bad "-Dir wrote $work/profile/.lockflight/bin"
bin="$work/profile/.lockflight/bin"
if out=$(install -Version "$version" 2> "$work/err") && [ "$(printf '%s\n' "$out" | tail -n 1)" = "$work/profile/.lockflight/tools/$version/lockflight.exe" ] \
  && [ "$(sha256 "$(printf '%s\n' "$out" | tail -n 1)")" = "$hash" ]; then
  ok "a first install by version takes the hash from engine-hashes.json, into ~/.lockflight/tools/<version>"
else
  bad "version install: $out $(cat "$work/err")"
fi
if [ "$(sha256 "$bin/lockflight.exe")" = "$hash" ] && [ "$(cat "$bin/lockflight.version")" = "$version" ] \
  && grep -q "claude plugin install lockflight@lockflight --scope user" "$work/err"; then
  ok "it completes the user install: bin/lockflight.exe, its version, and the plugin step"
else
  bad "user install: $(ls -A "$bin" 2>&1) $(cat "$work/err")"
fi
touch "$work/marker"
if out=$(install -Version "$version" 2> "$work/err") && grep -q "already installed" "$work/err" && grep -q "is Lockflight $version already" "$work/err" \
  && [ -z "$(find "$work/profile/.lockflight" -newer "$work/marker")" ]; then
  ok "a second install of the same version changes nothing"
else
  bad "second run: $(find "$work/profile/.lockflight" -newer "$work/marker") $(cat "$work/err")"
fi
if out=$(install -Lock "$work/second.lock" 2> "$work/err") && [ "$(sha256 "$work/profile/.lockflight/tools/$version2/lockflight.exe")" = "$hash2" ] \
  && [ "$(cat "$bin/lockflight.version")" = "$version" ] && grep -q "link --installed" "$work/err"; then
  ok "a lock of another version installs into tools and leaves the user engine, naming link --installed"
else
  bad "lock beside a user engine: $(cat "$bin/lockflight.version") $(cat "$work/err")"
fi
mkdir -p "$bin/.store/lockflight.cli" && printf '@echo off\n' > "$bin/lockflight.cmd" && printf '/a/checkout\n' > "$work/profile/.lockflight/source"
if out=$(install -Version "$version2" 2> "$work/err") && [ "$(sha256 "$bin/lockflight.exe")" = "$hash2" ] \
  && [ "$(cat "$bin/lockflight.version")" = "$version2" ] && [ ! -e "$bin/lockflight.cmd" ] && [ ! -e "$bin/.store" ] \
  && [ ! -e "$work/profile/.lockflight/source" ]; then
  ok "an install by version moves the user engine to it, removing a dotnet tool launcher and a recorded source checkout"
else
  bad "upgrade: $(ls -A "$bin") $(cat "$work/err")"
fi
status=0; install -Lock "$work/wrong.lock" -Dir "$work/wrong" > /dev/null 2> "$work/err" || status=$?
if [ "$status" = 1 ] && [ ! -e "$work/wrong/lockflight.exe" ] && grep -q "not the locked one" "$work/err"; then
  ok "a hash mismatch exits 1 and installs nothing"
else
  bad "mismatch: exit $status, $(cat "$work/err")"
fi
status=0; install > /dev/null 2>&1 || status=$?
[ "$status" = 4 ] && ok "neither a lock nor a version is a usage error (exit 4)" || bad "usage: exit $status"

[ "$failures" -eq 0 ] || { echo "$failures check(s) failed"; exit 1; }
echo "install.ps1 smoke test passed"
