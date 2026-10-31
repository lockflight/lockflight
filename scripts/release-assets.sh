#!/usr/bin/env bash
# The files of a judge release (release-packages.yml), named one by one, so the GitHub Release gets each exactly once. Dry
# runs use it too, so the step that publishes is exercised before a real tag needs it.
#   scripts/release-assets.sh checksums <dir> <version>
#       copies scripts/install.sh and scripts/install.ps1 into <dir> and writes <dir>/SHA256SUMS of the six packages, the five
#       executables, engine-hashes.json and the install scripts (the files the build provenance attestation covers).
#   scripts/release-assets.sh assemble <dir> <version> <out>
#       copies the sixteen release files from <dir> into the new, empty folder <out>: those above, SHA256SUMS and the SBOM
#       (lockflight-<version>.cdx.json). It fails when a file is missing, when two files would have the same name (GitHub
#       refuses a second asset of one name, after the packages are already on nuget.org), or when SHA256SUMS does not match.
# Run from the repository root.
set -euo pipefail

fail() { echo "release-assets: $*" >&2; exit 1; }

rids="linux-arm64 linux-x64 osx-arm64 osx-x64 win-x64"

# hashed <version>: the files SHA256SUMS lists, one per line.
hashed() {
  local version="$1" rid suffix
  echo "Lockflight.Cli.$version.nupkg"
  for rid in $rids; do echo "Lockflight.Cli.$rid.$version.nupkg"; done
  for rid in $rids; do
    suffix=""; case "$rid" in win-*) suffix=.exe ;; esac
    echo "lockflight-$version-$rid$suffix"
  done
  printf '%s\n' engine-hashes.json install.sh install.ps1
}

sha256() { if command -v sha256sum > /dev/null; then sha256sum "$@"; else shasum -a 256 "$@"; fi; }

[ $# -ge 3 ] || fail "usage: release-assets.sh checksums <dir> <version> | assemble <dir> <version> <out>"
command="$1" dir="$2" version="$3"
[ -d "$dir" ] || fail "$dir is not a folder"
[ -f scripts/install.sh ] || fail "run from the repository root"

case "$command" in
  checksums)
    cp scripts/install.sh scripts/install.ps1 "$dir/"
    names=()
    while IFS= read -r name; do
      [ -f "$dir/$name" ] || fail "$dir/$name is missing"
      names+=("$name")
    done < <(hashed "$version")
    (cd "$dir" && sha256 -- "${names[@]}" > SHA256SUMS)
    cat "$dir/SHA256SUMS"
    ;;
  assemble)
    [ $# -eq 4 ] || fail "usage: release-assets.sh assemble <dir> <version> <out>"
    out="$4"
    [ ! -e "$out" ] || [ -z "$(ls -A "$out")" ] || fail "$out is not empty"
    mkdir -p "$out"
    [ -f "$dir/SHA256SUMS" ] || fail "$dir/SHA256SUMS is missing"
    # The checksums were written in the publish job; the install scripts here must be the ones it hashed.
    cp scripts/install.sh scripts/install.ps1 "$dir/"
    (cd "$dir" && sha256 -c --quiet SHA256SUMS) || fail "$dir does not match its SHA256SUMS"
    listed=$(cut -d ' ' -f 3- "$dir/SHA256SUMS" | sed 's/^\*//' | sort)
    [ "$listed" = "$(hashed "$version" | sort)" ] || fail "SHA256SUMS lists other files than a release of $version has"
    while IFS= read -r name; do
      [ -f "$dir/$name" ] || fail "$dir/$name is missing"
      [ ! -e "$out/$name" ] || fail "$name would be uploaded twice"
      cp "$dir/$name" "$out/$name"
    done < <(hashed "$version"; printf '%s\n' SHA256SUMS "lockflight-$version.cdx.json")
    count=$(find "$out" -type f | wc -l | tr -d ' ')
    [ "$count" -eq 16 ] || fail "expected 16 release files, assembled $count"
    ls -1 "$out"
    ;;
  *) fail "unknown command $command" ;;
esac
