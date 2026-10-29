#!/bin/sh
# Lockflight engine install (POSIX sh): downloads the engine's RID package for this platform from nuget.org or a configured
# feed, extracts the executable, and checks its SHA-256 against a lock before anything runs it. No .NET SDK needed: sh, curl,
# one of unzip / bsdtar / python3 / perl (the .NET SDK images have only perl), and one of sha256sum / shasum.
#
#   install.sh [--lock <path>] [--version <v>] [--rid <rid>] [--dir <dir>] [--via-dotnet [--nuget-config <file>]]
#
#   --lock     a lockflight.engine-lock/v1 file: the version and the expected hash per platform (default:
#              .lockflight/engine.lock when it exists). Only a release lock is accepted.
#   --version  without a lock, for a person's first install: the hash comes from that version's engine-hashes.json in the
#              pointer package on the same feed. With neither, the script refuses.
#   --rid      linux-x64 | linux-arm64 | osx-x64 | osx-arm64 (default: detected; glibc Linux only)
#   --dir      where the executable goes (default: $LOCKFLIGHT_TEMP/lockflight when LOCKFLIGHT_TEMP is set, else
#              ~/.lockflight/tools/<version>)
#   --via-dotnet  fetch the package with `dotnet tool install` (for feeds that need a NuGet credential provider, or that only a
#              nuget.config names), then run the same hash check; --nuget-config passes that file to dotnet
#
# Environment: LOCKFLIGHT_NUGET_FEED (a flat-container base or a v3 index.json; https:// only, file:// for tests),
# LOCKFLIGHT_NUGET_USER and LOCKFLIGHT_NUGET_TOKEN (basic auth, given to curl on stdin, never on its command line),
# LOCKFLIGHT_RELEASE_FALLBACK=1 (download the bare executable from the GitHub Release of the version instead of the feed).
#
# The last line of output is the installed executable's path. Exit 0 installed, 1 failed, 4 usage.
set -eu

default_feed="https://api.nuget.org/v3-flatcontainer/"
release_base="https://github.com/lockflight/lockflight/releases/download"
supported="linux-x64, linux-arm64, osx-x64, osx-arm64"

say() { printf 'lockflight install: %s\n' "$*" >&2; }
fail() { say "$*"; exit 1; }
usage() { say "$*"; say "usage: install.sh [--lock <path>] [--version <v>] [--rid <rid>] [--dir <dir>] [--via-dotnet [--nuget-config <file>]]"; exit 4; }

# json_string <path>: the string at a top-level key ("cwd") or one level down ("tool_input.command"), unescaped. Tracks
# nesting and strings, so a key nested elsewhere ({"x":{"tool_name":"Read"}}) or inside a string value never matches.
json_string() {
  printf '%s' "$input" | tr '\n\r' '  ' | awk -v want="$1" '{
    s = $0; n = length(s); depth = 0; instr = 0; buf = ""; iskey = 0
    for (i = 1; i <= n; i++) {
      c = substr(s, i, 1)
      if (instr) {
        if (c == "\\") {
          i++; e = substr(s, i, 1)
          if (e == "n") buf = buf "\n"; else if (e == "t") buf = buf "\t"; else if (e == "u") buf = buf "\\u"; else buf = buf e
          continue
        }
        if (c == "\"") {
          instr = 0
          if (iskey) { key[depth] = buf; continue }
          if (kind[depth] != "o") continue
          path = key[1]; for (d = 2; d <= depth; d++) path = path "." key[d]
          if (path == want) { printf "%s", buf; exit }
          continue
        }
        buf = buf c; continue
      }
      if (c == "\"") { instr = 1; buf = ""; iskey = (kind[depth] == "o" && expectkey[depth]); continue }
      if (c == "{") { depth++; kind[depth] = "o"; expectkey[depth] = 1; key[depth] = ""; continue }
      if (c == "[") { depth++; kind[depth] = "a"; expectkey[depth] = 0; continue }
      if (c == "}" || c == "]") { depth--; continue }
      if (c == ":") { expectkey[depth] = 0; continue }
      if (c == "," && kind[depth] == "o") expectkey[depth] = 1
    }
  }'
}

lock="" version="" rid="" dir="" via_dotnet=0 nuget_config=""
while [ $# -gt 0 ]; do
  case "$1" in
    --lock|--version|--rid|--dir|--nuget-config)
      [ $# -ge 2 ] && [ -n "$2" ] || usage "$1 needs a value"
      case "$1" in
        --lock) lock="$2" ;;
        --version) version="$2" ;;
        --rid) rid="$2" ;;
        --dir) dir="$2" ;;
        --nuget-config) nuget_config="$2" ;;
      esac
      shift 2 ;;
    --via-dotnet) via_dotnet=1; shift ;;
    *) usage "unknown argument '$1'" ;;
  esac
done
if [ -z "$lock" ] && [ -z "$version" ] && [ -f .lockflight/engine.lock ]; then lock=.lockflight/engine.lock; fi
[ -n "$lock" ] || [ -n "$version" ] || usage "give --lock <engine.lock>, or --version <v> for a first install without a lock"
[ -z "$nuget_config" ] || [ "$via_dotnet" = 1 ] || usage "--nuget-config needs --via-dotnet"

# The platform. Never uname -p or a process per check beyond these two; glibc only, since the packages are glibc builds.
if [ -z "$rid" ]; then
  system=$(uname -s) machine=$(uname -m)
  case "$system/$machine" in
    Darwin/arm64) rid=osx-arm64 ;;
    Darwin/x86_64) rid=osx-x64 ;;
    Linux/x86_64) rid=linux-x64 ;;
    Linux/aarch64|Linux/arm64) rid=linux-arm64 ;;
    *) fail "no Lockflight engine for $system $machine (supported: $supported; on Windows use install.ps1)" ;;
  esac
fi
case "$rid" in
  linux-x64|linux-arm64|osx-x64|osx-arm64) ;;
  *) fail "no Lockflight engine for '$rid' (supported: $supported; on Windows use install.ps1)" ;;
esac
case "$rid" in
  linux-*)
    if ls /lib/ld-musl-* >/dev/null 2>&1 || (ldd --version 2>&1 | grep -qi musl); then
      fail "this Linux uses musl libc (Alpine); the engine is built for glibc. Use a glibc image such as Debian or Ubuntu"
    fi ;;
esac

# The expected hash: from the lock, or from the version's engine-hashes.json (fetched below).
expected=""
if [ -n "$lock" ]; then
  [ -f "$lock" ] || fail "no lock at $lock"
  input=$(cat "$lock")
  [ "$(json_string schema)" = "lockflight.engine-lock/v1" ] || fail "$lock is not a lockflight.engine-lock/v1 file"
  channel=$(json_string channel)
  [ "$channel" = release ] || fail "$lock is a '$channel' lock (a local development build); CI and this script install only release locks"
  locked=$(json_string version)
  [ -z "$version" ] || [ "$version" = "$locked" ] || fail "--version $version differs from the lock's $locked"
  version="$locked"
  expected=$(json_string "binaries.$rid.sha256")
  [ -n "$expected" ] || fail "$lock has no hash for $rid"
fi
case "$version" in
  *[!0-9A-Za-z.-]*|"") fail "'$version' is not an engine version" ;;
esac
lower=$(printf '%s' "$version" | tr '[:upper:]' '[:lower:]')

if [ -z "$dir" ]; then
  if [ -n "${LOCKFLIGHT_TEMP:-}" ]; then dir="$LOCKFLIGHT_TEMP/lockflight"; else dir="$HOME/.lockflight/tools/$version"; fi
fi
mkdir -p "$dir"
work=$(mktemp -d "${TMPDIR:-/tmp}/lockflight-install.XXXXXX")
trap 'rm -rf "$work"' EXIT

# fetch <url> <file> [plain]: curl without credentials on its command line. Feed credentials go in a config on stdin (never to
# the GitHub Release: plain); redirects follow only to https.
fetch() {
  case "$1" in
    https://*|file://*) ;;
    *) fail "refusing to download $1: only https:// feeds (and file:// for tests)" ;;
  esac
  # ${…:+set}: a test of the token itself would show it under set -x.
  if [ "${3:-}" != plain ] && [ -n "${LOCKFLIGHT_NUGET_USER:+set}" ] && [ -n "${LOCKFLIGHT_NUGET_TOKEN:+set}" ]; then
    case "$LOCKFLIGHT_NUGET_USER$LOCKFLIGHT_NUGET_TOKEN" in
      *'"'*|*'\'*|*"
"*) fail "LOCKFLIGHT_NUGET_USER and LOCKFLIGHT_NUGET_TOKEN cannot hold a quote, a backslash or a line break" ;;
    esac
    # A here-document: neither set -x nor the process list shows its text.
    curl -fsSL --proto '=https,file' --proto-redir '=https' --config - -o "$2" "$1" <<CONFIG
user = "$LOCKFLIGHT_NUGET_USER:$LOCKFLIGHT_NUGET_TOKEN"
CONFIG
  else
    curl -fsSL --proto '=https,file' --proto-redir '=https' -o "$2" "$1"
  fi
}

# The flat-container base of the feed: the default, a base given as is, or the PackageBaseAddress of a v3 service index.
feed_base() {
  feed="${LOCKFLIGHT_NUGET_FEED:-$default_feed}"
  case "$feed" in
    */index.json)
      fetch "$feed" "$work/index.json" || fail "could not read the service index $feed"
      base=$(tr '\n\r' '  ' < "$work/index.json" | awk '{
        n = split($0, parts, "}")
        for (i = 1; i <= n; i++) {
          if (parts[i] ~ /"@type"[ \t]*:[ \t]*"PackageBaseAddress\/3\.0\.0"/) {
            if (match(parts[i], /"@id"[ \t]*:[ \t]*"[^"]*"/)) {
              s = substr(parts[i], RSTART, RLENGTH); sub(/^"@id"[ \t]*:[ \t]*"/, "", s); sub(/"$/, "", s); print s; exit
            }
          }
        }
      }')
      [ -n "$base" ] || fail "the service index $feed has no PackageBaseAddress/3.0.0" ;;
    *) base="$feed" ;;
  esac
  case "$base" in */) ;; *) base="$base/" ;; esac
  printf '%s' "$base"
}

# entry_of <nupkg>: the one tools/<tfm>/<rid>/lockflight entry; never one with '..' or an absolute path.
entries() {
  if command -v unzip >/dev/null 2>&1; then unzip -Z1 "$1"
  elif command -v bsdtar >/dev/null 2>&1; then bsdtar -tf "$1"
  elif command -v python3 >/dev/null 2>&1; then python3 -I -c 'import sys, zipfile; print("\n".join(zipfile.ZipFile(sys.argv[1]).namelist()))' "$1"
  elif perl -MIO::Uncompress::Unzip -e 1 >/dev/null 2>&1; then
    perl -MIO::Uncompress::Unzip -e 'my $z = IO::Uncompress::Unzip->new($ARGV[0]) or exit 1;
      for (my $s = 1; $s > 0; $s = $z->nextStream) { print $z->getHeaderInfo->{Name}, "\n" }' "$1"
  else fail "no unzip, bsdtar, python3 or perl to open the package; install one of them"
  fi
}
extract() {
  if command -v unzip >/dev/null 2>&1; then unzip -p "$1" "$2" > "$3"
  elif command -v bsdtar >/dev/null 2>&1; then bsdtar -xOf "$1" "$2" > "$3"
  elif command -v python3 >/dev/null 2>&1; then
    python3 -I -c 'import sys, zipfile; sys.stdout.buffer.write(zipfile.ZipFile(sys.argv[1]).read(sys.argv[2]))' "$1" "$2" > "$3"
  else
    perl -MIO::Uncompress::Unzip=unzip,\$UnzipError -e 'unzip($ARGV[0] => $ARGV[2], Name => $ARGV[1]) or die "$UnzipError\n"' "$1" "$2" "$3"
  fi
}
entry_of() {
  entries "$1" > "$work/entries" || fail "$1 is not a package"
  matches=$(grep -E "^tools/[^/]+/$rid/lockflight\$" "$work/entries" || true)
  count=$(printf '%s' "$matches" | grep -c . || true)
  [ "$count" = 1 ] || fail "$1 holds $count tools/<tfm>/$rid/lockflight entries, expected one"
  case "$matches" in
    /*|*../*|*/..*) fail "$1: refusing the entry $matches" ;;
  esac
  printf '%s' "$matches"
}
sha256_of() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d ' ' -f 1
  elif command -v shasum >/dev/null 2>&1; then shasum -a 256 "$1" | cut -d ' ' -f 1
  else fail "no sha256sum or shasum to check the engine; install one of them"
  fi
}

if [ -z "$expected" ]; then
  base=$(feed_base)
  fetch "${base}lockflight.cli/$lower/lockflight.cli.$lower.nupkg" "$work/pointer.nupkg" \
    || fail "could not download Lockflight.Cli $version from ${base}"
  entries "$work/pointer.nupkg" | grep -qx engine-hashes.json || fail "Lockflight.Cli $version has no engine-hashes.json (released before 0.3.0?)"
  extract "$work/pointer.nupkg" engine-hashes.json "$work/engine-hashes.json"
  input=$(cat "$work/engine-hashes.json")
  [ "$(json_string schema)" = "lockflight.engine-hashes/v1" ] || fail "engine-hashes.json of $version is not lockflight.engine-hashes/v1"
  [ "$(json_string version)" = "$version" ] || fail "engine-hashes.json of $version describes $(json_string version)"
  expected=$(json_string "binaries.$rid.sha256")
  [ -n "$expected" ] || fail "engine-hashes.json of $version has no hash for $rid"
fi
case "$expected" in
  *[!0-9a-f]*) fail "the expected hash '$expected' is not lower-case hex" ;;
esac
[ "${#expected}" = 64 ] || fail "the expected hash '$expected' is not a SHA-256"

candidate="$work/lockflight"
if [ "${LOCKFLIGHT_RELEASE_FALLBACK:-}" = 1 ]; then
  fetch "$release_base/v$version/lockflight-$version-$rid" "$candidate" plain \
    || fail "could not download lockflight-$version-$rid from the GitHub Release v$version"
elif [ "$via_dotnet" = 1 ]; then
  command -v dotnet >/dev/null 2>&1 || fail "--via-dotnet needs dotnet on PATH"
  set -- tool install Lockflight.Cli --version "$version" --tool-path "$work/dotnet-tool"
  [ -z "$nuget_config" ] || set -- "$@" --configfile "$nuget_config"
  (cd "$work" && dotnet "$@" >&2) || fail "dotnet tool install Lockflight.Cli $version failed"
  found=$(find "$work/dotnet-tool/.store" -type f -path "*/tools/*/$rid/lockflight" | head -n 2)
  [ "$(printf '%s\n' "$found" | grep -c .)" = 1 ] || fail "dotnet installed no single tools/<tfm>/$rid/lockflight"
  cp "$found" "$candidate"
else
  base=$(feed_base)
  package="$work/lockflight.cli.$rid.nupkg"
  fetch "${base}lockflight.cli.$rid/$lower/lockflight.cli.$rid.$lower.nupkg" "$package" \
    || fail "could not download Lockflight.Cli.$rid $version from ${base}"
  entry=$(entry_of "$package")
  extract "$package" "$entry" "$candidate"
fi

# Verify before anything runs it: the script never executes the engine, not even to ask its version.
actual=$(sha256_of "$candidate")
if [ "$actual" != "$expected" ]; then
  rm -f "$candidate"
  fail "the downloaded engine is not the locked one: expected $expected, got $actual (deleted)"
fi
chmod 755 "$candidate"
target="$dir/lockflight"
staged="$dir/.lockflight.$$"
cp "$candidate" "$staged" && chmod 755 "$staged" && mv -f "$staged" "$target" || { rm -f "$staged"; fail "could not write $target"; }
say "installed Lockflight $version ($rid, sha256 $actual)"
printf '%s\n' "$target"
