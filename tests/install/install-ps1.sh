#!/usr/bin/env bash
# A smoke test of scripts/install.ps1, the Windows install a person runs once (a Release asset; no CI template uses it), with
# PowerShell 7 against a local file:// feed: a lock installs the win-x64 executable, a first install by version takes the hash
# from engine-hashes.json, a hash mismatch installs nothing. Skipped where pwsh is missing. Windows PowerShell 5.1 is not run here.
set -euo pipefail

command -v pwsh >/dev/null || { echo "skipped: no pwsh"; exit 0; }
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

install() { LOCKFLIGHT_NUGET_FEED="file://$feed/" HOME="$work/home" pwsh -NoProfile -File "$root/scripts/install.ps1" "$@"; }

if out=$(install -Lock "$work/engine.lock" -Dir "$work/out" 2> "$work/err") && [ "$(printf '%s\n' "$out" | tail -n 1)" = "$work/out/lockflight.exe" ] \
  && [ "$(sha256 "$work/out/lockflight.exe")" = "$hash" ]; then
  ok "a lock installs the locked win-x64 engine and prints its path last"
else
  bad "lock install: $out $(cat "$work/err")"
fi
if out=$(install -Version "$version" 2> "$work/err") && [ "$(sha256 "$(printf '%s\n' "$out" | tail -n 1)")" = "$hash" ]; then
  ok "a first install by version takes the hash from engine-hashes.json"
else
  bad "version install: $out $(cat "$work/err")"
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
