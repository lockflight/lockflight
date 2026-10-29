#!/usr/bin/env bash
# scripts/install.sh against a local file:// feed of hand-made packages, under every POSIX shell available (dash, bash,
# BusyBox sh; SHELLS="dash bash" picks): the happy path from a lock, a first install by version, a v3 service index, a hash
# mismatch (the file is deleted), a zip-slip entry, an unknown platform, a development lock, musl, no unzip tool, credentials
# kept out of the command line and of set -x, and --via-dotnet with a stand-in dotnet. The script must never run the engine.
# INSTALL_SCRIPT=<path> tests another copy.
set -euo pipefail

root=$(cd "$(dirname "$0")/../.." && pwd)
script="${INSTALL_SCRIPT:-$root/scripts/install.sh}"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
version=0.0.0-ci.7
failures=0

ok() { echo "ok   [$shell] $1"; }
# says <text> <label>: the last run's stderr holds the text.
says() { if grep -q -- "$1" "$work/stderr"; then ok "$2"; else bad "$2: stderr was $(tr '\n' ' ' < "$work/stderr")"; fi; }
bad() { echo "FAIL [$shell] $1"; failures=$((failures + 1)); }

sha256() { if command -v sha256sum >/dev/null; then sha256sum "$1" | cut -d ' ' -f 1; else shasum -a 256 "$1" | cut -d ' ' -f 1; fi; }

# make_package <path> <entry> <@file|content>...: a deflated zip, written with python3 or, where there is none (the .NET SDK
# images), perl.
make_package() {
  if command -v python3 >/dev/null; then
    python3 -I - "$@" <<'PY'
import sys, zipfile
path, rest = sys.argv[1], sys.argv[2:]
with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as archive:
    for entry, content in zip(rest[::2], rest[1::2]):
        archive.writestr(entry, open(content[1:], "rb").read() if content.startswith("@") else content)
PY
  else
    perl -MIO::Compress::Zip=zip,\$ZipError -e '
      my ($path, @rest) = @ARGV; my $first = 1; my $z;
      while (my ($entry, $content) = splice(@rest, 0, 2)) {
        if ($content =~ s/^@//) { local $/; open(my $f, "<:raw", $content) or die; $content = <$f>; }
        if ($first) { $z = IO::Compress::Zip->new($path, Name => $entry) or die $ZipError; $first = 0 }
        else { $z->newStream(Name => $entry) }
        $z->print($content);
      }
      $z->close;' "$@"
  fi
}

case "$(uname -s)/$(uname -m)" in
  Darwin/arm64) rid=osx-arm64 ;;
  Darwin/x86_64) rid=osx-x64 ;;
  Linux/x86_64) rid=linux-x64 ;;
  Linux/aarch64) rid=linux-arm64 ;;
  *) echo "skipped: no engine platform for $(uname -s) $(uname -m)"; exit 0 ;;
esac

# The engine stand-in leaves a mark if anything runs it.
feed="$work/feed"
mkdir -p "$feed"
printf '#!/bin/sh\ntouch "%s/engine-ran"\n' "$work" > "$work/engine"
engine_hash=$(sha256 "$work/engine")
lock_json() { # lock_json <channel> <hash>
  printf '{\n  "schema": "lockflight.engine-lock/v1",\n  "package": "Lockflight.Cli",\n  "version": "%s",\n  "channel": "%s",\n  "installScript": { "sha256": "%s" },\n  "binaries": {\n    "%s": { "sha256": "%s" }\n  }\n}\n' \
    "$version" "$1" "$(printf '0%.0s' $(seq 64))" "$rid" "$2"
}
lock_json release "$engine_hash" > "$work/engine.lock"
lock_json development "$engine_hash" > "$work/dev.lock"
lock_json release "$(printf 'a%.0s' $(seq 64))" > "$work/wrong.lock"
mkdir -p "$feed/lockflight.cli.$rid/$version" "$feed/lockflight.cli/$version"
make_package "$feed/lockflight.cli.$rid/$version/lockflight.cli.$rid.$version.nupkg" "tools/any/$rid/lockflight" "@$work/engine" "tools/any/$rid/DotnetToolSettings.xml" "<x/>"
printf '{\n  "schema": "lockflight.engine-hashes/v1",\n  "package": "Lockflight.Cli",\n  "version": "%s",\n  "commit": "%s",\n  "binaries": {\n    "%s": {\n      "sha256": "%s",\n      "path": "tools/any/%s/lockflight"\n    }\n  }\n}\n' \
  "$version" "$(printf '1%.0s' $(seq 40))" "$rid" "$engine_hash" "$rid" > "$work/engine-hashes.json"
make_package "$feed/lockflight.cli/$version/lockflight.cli.$version.nupkg" engine-hashes.json "@$work/engine-hashes.json"
mkdir -p "$work/index"
printf '{ "version": "3.0.0", "resources": [\n  { "@id": "file://%s/nope/", "@type": "SearchQueryService" },\n  { "@id": "file://%s/", "@type": "PackageBaseAddress/3.0.0", "comment": "x" }\n] }\n' "$feed" "$feed" > "$work/index/index.json"
slip="$work/slip-feed"
mkdir -p "$slip/lockflight.cli.$rid/$version"
make_package "$slip/lockflight.cli.$rid/$version/lockflight.cli.$rid.$version.nupkg" "tools/../$rid/lockflight" "@$work/engine"

# Stand-ins: a curl that records its arguments and stdin, an ldd that reports musl, a dotnet that lays out a tool store.
mkdir -p "$work/spy" "$work/musl" "$work/dotnet"
real_curl=$(command -v curl)
printf '#!/bin/sh\nprintf "%%s\\n" "$*" >> "%s/curl-args"\ncat >> "%s/curl-stdin" 2>/dev/null || true\nexec "%s" "$@" </dev/null\n' "$work" "$work" "$real_curl" > "$work/spy/curl"
printf '#!/bin/sh\necho "musl libc (x86_64)"\n' > "$work/musl/ldd"
cat > "$work/dotnet/dotnet" <<DOTNET
#!/bin/sh
printf '%s\n' "\$*" > "$work/dotnet-args"
while [ \$# -gt 0 ]; do [ "\$1" = --tool-path ] && path="\$2"; shift; done
store="\$path/.store/lockflight.cli.$rid/$version/lockflight.cli.$rid/$version/tools/any/$rid"
mkdir -p "\$store" && cp "$work/engine" "\$store/lockflight"
DOTNET
chmod +x "$work/spy/curl" "$work/musl/ldd" "$work/dotnet/dotnet" "$work/engine"
# A PATH with the tools the script needs but no unzip, bsdtar, python3 or perl.
mkdir -p "$work/bare"
for tool in sh awk tr grep cut mkdir mktemp rm cp mv chmod cat uname find head ls sha256sum shasum curl dirname basename seq; do
  found=$(command -v "$tool" 2>/dev/null || true)
  [ -n "$found" ] && [ "${found#/}" != "$found" ] && ln -sf "$found" "$work/bare/$tool"
done

# run <name> <expected exit> [env assignments...] -- <install.sh arguments...>: runs the script in a fresh directory.
run() {
  local name="$1" expected="$2"; shift 2
  local assignments=()
  while [ "$1" != -- ]; do assignments+=("$1"); shift; done
  shift
  rm -rf "$work/case" "$work/engine-ran" && mkdir -p "$work/case"
  local status=0
  (cd "$work/case" && env HOME="$work/case/home" LOCKFLIGHT_NUGET_FEED="file://$feed/" ${assignments[@]+"${assignments[@]}"} $shell "$script" "$@") \
    > "$work/stdout" 2> "$work/stderr" || status=$?
  if [ "$status" != "$expected" ]; then
    bad "$name: exit $status, expected $expected; stderr: $(tr '\n' ' ' < "$work/stderr")"
    return 1
  fi
  if [ -e "$work/engine-ran" ]; then bad "$name: the script ran the engine"; return 1; fi
  return 0
}

shells="${SHELLS:-}"
if [ -z "$shells" ]; then
  for candidate in dash bash; do command -v "$candidate" >/dev/null && shells="$shells $candidate"; done
  command -v busybox >/dev/null && shells="$shells busybox-sh"
fi

for shell_name in $shells; do
  # Absolute, so a run with a reduced PATH still finds the shell.
  shell=$(command -v "${shell_name%-sh}")
  [ "$shell_name" = busybox-sh ] && shell="$shell sh"

  if run "lock" 0 -- --lock "$work/engine.lock" --dir "$work/case/out"; then
    installed=$(tail -n 1 "$work/stdout")
    if [ "$installed" = "$work/case/out/lockflight" ] && [ "$(sha256 "$installed")" = "$engine_hash" ] && [ -x "$installed" ]; then
      ok "a lock installs the locked engine, executable, and prints its path last"
    else
      bad "lock install printed '$installed'"
    fi
  fi

  run "nothing given" 4 -- --dir "$work/case/out" \
    && says "give --lock" "neither a lock nor a version is a usage error"

  if run "version" 0 -- --version "$version"; then
    [ "$(tail -n 1 "$work/stdout")" = "$work/case/home/.lockflight/tools/$version/lockflight" ] \
      && ok "a first install by version takes the hash from engine-hashes.json and goes to ~/.lockflight/tools/<version>" \
      || bad "version install printed $(tail -n 1 "$work/stdout")"
  fi

  run "index" 0 LOCKFLIGHT_NUGET_FEED="file://$work/index/index.json" -- --lock "$work/engine.lock" --dir "$work/case/out" \
    && ok "a v3 service index resolves to its PackageBaseAddress"

  run "LOCKFLIGHT_TEMP" 0 LOCKFLIGHT_TEMP="$work/case/ci" -- --lock "$work/engine.lock" \
    && [ "$(tail -n 1 "$work/stdout")" = "$work/case/ci/lockflight/lockflight" ] && ok "LOCKFLIGHT_TEMP sets the default directory"

  if run "mismatch" 1 -- --lock "$work/wrong.lock" --dir "$work/case/out"; then
    if [ ! -e "$work/case/out/lockflight" ] && grep -q "expected aaaa" "$work/stderr" && grep -q "$engine_hash" "$work/stderr"; then
      ok "a hash mismatch exits 1 naming both hashes and installs nothing"
    else
      bad "mismatch left a file or did not name both hashes: $(cat "$work/stderr")"
    fi
  fi

  run "zip-slip" 1 LOCKFLIGHT_NUGET_FEED="file://$slip/" -- --lock "$work/engine.lock" --dir "$work/case/out" \
    && says "refusing the entry" "an entry that leaves the directory is refused"

  run "unknown rid" 1 -- --lock "$work/engine.lock" --rid linux-musl-x64 --dir "$work/case/out" \
    && says "supported: linux-x64" "an unknown platform is refused with the supported list"

  run "development lock" 1 -- --lock "$work/dev.lock" --dir "$work/case/out" \
    && says "'development' lock" "a development lock is refused"

  if [ "$rid" = linux-x64 ] || [ "$rid" = linux-arm64 ]; then
    run "musl" 1 PATH="$work/musl:$PATH" -- --lock "$work/engine.lock" --dir "$work/case/out" \
      && says "musl" "musl libc is refused by name"
  fi

  for tool in unzip bsdtar python3 perl; do
    found=$(command -v "$tool" 2>/dev/null || true)
    [ -n "$found" ] || continue
    rm -rf "$work/only" && cp -R "$work/bare" "$work/only" && ln -sf "$found" "$work/only/$tool"
    run "only $tool" 0 PATH="$work/only" -- --lock "$work/engine.lock" --dir "$work/case/out" \
      && [ "$(sha256 "$work/case/out/lockflight")" = "$engine_hash" ] && ok "the package opens with $tool alone"
  done

  run "no unzip" 1 PATH="$work/bare" -- --lock "$work/engine.lock" --dir "$work/case/out" \
    && says "no unzip, bsdtar, python3 or perl" "without an unzip tool it refuses, naming them"

  rm -f "$work/curl-args" "$work/curl-stdin"
  if run "credentials" 0 PATH="$work/spy:$PATH" LOCKFLIGHT_NUGET_USER=alice LOCKFLIGHT_NUGET_TOKEN=s3cr3t-t0ken -- --lock "$work/engine.lock" --dir "$work/case/out"; then
    if grep -q "s3cr3t-t0ken" "$work/curl-args"; then bad "the token was on curl's command line"
    elif ! grep -q 'user = "alice:s3cr3t-t0ken"' "$work/curl-stdin"; then bad "curl did not get the credentials on stdin"
    else ok "credentials reach curl on stdin, never on its command line"
    fi
  fi
  if [ "$shell_name" != busybox-sh ]; then
    (cd "$work/case" && env LOCKFLIGHT_NUGET_FEED="file://$feed/" LOCKFLIGHT_NUGET_USER=alice LOCKFLIGHT_NUGET_TOKEN=s3cr3t-t0ken \
      $shell -x "$script" --lock "$work/engine.lock" --dir "$work/case/out") > "$work/trace" 2>&1 || true
    if grep -q "s3cr3t-t0ken" "$work/trace"; then bad "set -x shows the token"; else ok "set -x does not show the token"; fi
  fi

  run "refuse http" 1 LOCKFLIGHT_NUGET_FEED="http://example.invalid/" -- --lock "$work/engine.lock" --dir "$work/case/out" \
    && says "only https://" "an http:// feed is refused"

  if run "via dotnet" 0 PATH="$work/dotnet:$PATH" -- --lock "$work/engine.lock" --dir "$work/case/out" --via-dotnet --nuget-config "$work/nuget.config"; then
    if grep -q -- "--configfile $work/nuget.config" "$work/dotnet-args" && [ "$(sha256 "$work/case/out/lockflight")" = "$engine_hash" ]; then
      ok "--via-dotnet installs through dotnet with the nuget.config and checks the same hash"
    else
      bad "--via-dotnet: $(cat "$work/dotnet-args")"
    fi
  fi

  run "usage" 4 -- --lock && ok "a missing value is a usage error (exit 4)"
done

[ -n "$shells" ] || { echo "no POSIX shell to test with"; exit 1; }
[ "$failures" -eq 0 ] || { echo "$failures check(s) failed"; exit 1; }
echo "all install script checks passed"
