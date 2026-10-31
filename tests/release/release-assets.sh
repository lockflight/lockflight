#!/usr/bin/env bash
# scripts/release-assets.sh on a hand-made artifact folder, laid out as download-artifact merges a release run's artifacts:
# the sixteen release files are assembled once each (the SBOM matches no other name), and a missing file, a file changed
# after its checksum or a used output folder fails.
set -euo pipefail

root=$(cd "$(dirname "$0")/../.." && pwd)
script="$root/scripts/release-assets.sh"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
rids="linux-arm64 linux-x64 osx-arm64 osx-x64 win-x64"
failures=0

ok() { echo "ok   $1"; }
bad() { echo "FAIL $1"; failures=$((failures + 1)); }
run() { (cd "$root" && bash "$script" "$@") > "$work/out" 2>&1; }
expect_failure() {
  local label="$1"; shift
  if run "$@"; then bad "$label (it passed)"; elif grep -q "$label_pattern" "$work/out"; then ok "$label"; else bad "$label: $(cat "$work/out")"; fi
}

# artifacts <dir> <version>: every artifact of a release run, merged into one folder.
artifacts() {
  local dir="$1" version="$2" rid suffix
  mkdir -p "$dir"
  echo pointer > "$dir/Lockflight.Cli.$version.nupkg"
  for rid in $rids; do
    suffix=""; case "$rid" in win-*) suffix=.exe ;; esac
    echo "package $rid" > "$dir/Lockflight.Cli.$rid.$version.nupkg"
    echo "engine $rid" > "$dir/lockflight-$version-$rid$suffix"
    echo "{}" > "$dir/hash-$rid.json"
  done
  echo '{"schema":"lockflight.engine-hashes/v1"}' > "$dir/engine-hashes.json"
  echo '{"bomFormat":"CycloneDX"}' > "$dir/lockflight-$version.cdx.json"
}

for version in 0.3.0 0.3.0-rc.1; do
  artifacts "$work/$version" "$version"
  if run checksums "$work/$version" "$version" && [ "$(wc -l < "$work/$version/SHA256SUMS" | tr -d ' ')" -eq 14 ]; then
    ok "$version: SHA256SUMS lists the packages, executables, hashes and install scripts"
  else
    bad "$version: checksums: $(cat "$work/out")"
  fi
  if run assemble "$work/$version" "$version" "$work/assets-$version"; then
    names=$(ls -1 "$work/assets-$version")
    if [ "$(echo "$names" | wc -l | tr -d ' ')" -eq 16 ] && echo "$names" | grep -qx "lockflight-$version.cdx.json" \
      && echo "$names" | grep -qx SHA256SUMS && ! echo "$names" | grep -q '^hash-'; then
      ok "$version: sixteen release files, the SBOM once, no build-only files"
    else
      bad "$version: assembled $names"
    fi
  else
    bad "$version: assemble: $(cat "$work/out")"
  fi
done

label_pattern="not empty"
expect_failure "a used output folder is refused" assemble "$work/0.3.0" 0.3.0 "$work/assets-0.3.0"

label_pattern="does not match"
echo changed > "$work/0.3.0/lockflight-0.3.0-linux-x64"
expect_failure "a file changed after its checksum is refused" assemble "$work/0.3.0" 0.3.0 "$work/assets-changed"

artifacts "$work/no-sbom" 0.3.0
run checksums "$work/no-sbom" 0.3.0
rm "$work/no-sbom/lockflight-0.3.0.cdx.json"
label_pattern="cdx.json is missing"
expect_failure "a missing SBOM is refused" assemble "$work/no-sbom" 0.3.0 "$work/assets-no-sbom"

artifacts "$work/no-exe" 0.3.0
rm "$work/no-exe/lockflight-0.3.0-win-x64.exe"
label_pattern="win-x64.exe is missing"
expect_failure "a missing executable fails the checksums" checksums "$work/no-exe" 0.3.0

label_pattern="other files"
expect_failure "checksums of another version are refused" assemble "$work/0.3.0-rc.1" 0.3.0 "$work/assets-other"

if [ "$failures" -gt 0 ]; then echo "$failures failure(s)"; exit 1; fi
echo "all release-assets checks passed"
