#!/usr/bin/env bash
# A Lockflight 0.2 engine meeting a version 2 manifest (a teammate who has not updated yet, after the migration merged): it
# refuses the manifest with "'version' must be 1", allows reads and blocks every other call, the 0.2 recovery command
# included, so the fix is a person's `~/.lockflight/bin/lockflight update` in a terminal (the troubleshooting guide says so). Needs the network (it installs Lockflight.Cli 0.2.0-alpha.9 from nuget.org with dotnet). Run by hand before a release.
set -euo pipefail

old_version="${OLD_VERSION:-0.2.0-alpha.9}"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
failures=0
ok() { echo "ok   $1"; }
bad() { echo "FAIL $1"; failures=$((failures + 1)); }

(cd "$work" && dotnet tool install Lockflight.Cli --version "$old_version" --tool-path "$work/tool" >/dev/null)
old="$work/tool/lockflight"
mkdir -p "$work/project" && cd "$work/project" && git init -q
printf 'version: 2\nrouting:\n  preset: default\n' > lockflight.yaml

doctor=$("$old" doctor 2>&1 || true)
case "$doctor" in *"'version' must be 1"*) ok "doctor names the version it reads" ;; *) bad "doctor: $doctor" ;; esac
status=0; echo '{"tool_name":"Read","tool_input":{"file_path":"a"}}' | "$old" hook pre-tool-use >/dev/null 2>&1 || status=$?
[ "$status" = 0 ] && ok "a read is allowed" || bad "read: exit $status"
status=0; echo '{"tool_name":"Write","tool_input":{"file_path":"a"}}' | "$old" hook pre-tool-use > /dev/null 2> "$work/err" || status=$?
if [ "$status" = 2 ] && grep -q "'version' must be 1" "$work/err"; then ok "a write is blocked, naming the version"; else bad "write: exit $status"; fi
status=0; echo '{"tool_name":"Bash","tool_input":{"command":"dotnet tool restore && dotnet lockflight link"}}' | "$old" hook pre-tool-use >/dev/null 2>&1 || status=$?
[ "$status" = 2 ] && ok "the 0.2 recovery command is blocked too: only reading is left in a session" || bad "recovery: exit $status"

[ "$failures" -eq 0 ] || { echo "$failures check(s) failed"; exit 1; }
echo "cross-version checks passed ($old_version on a version 2 manifest)"
