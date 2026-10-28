#!/bin/sh
# LLM-free compatibility suite for the plugin's hook shim + the engine: feeds Claude Code-shaped hook JSON, checks exit codes.
# Usage: ENGINE=/path/to/lockflight tests/compat/probe-suite.sh     (POSIX sh; macOS, Linux, Git Bash on Windows)
HERE=$(cd "$(dirname "$0")" && pwd); SHIM="$HERE/../../plugins/lockflight/bin/lockflight-hook"
unset CLAUDE_PROJECT_DIR
WORK=$(mktemp -d); pass=0; fail=0
check() { # $1=name $2=expected exit $3=dir $4=json
  printf '%s' "$4" | LOCKFLIGHT_DEBUG=1 "$SHIM" pre-tool-use >/dev/null 2>"$WORK/err"; got=$?
  if [ "$got" = "$2" ]; then pass=$((pass+1)); r=ok; else fail=$((fail+1)); r="FAIL ($(head -c 400 "$WORK/err"))"; fi
  printf '%-44s expected=%s got=%s %s\n' "$1" "$2" "$got" "$r"
}
# On Git Bash, hand the engine Windows-style JSON paths, as Claude Code does on Windows.
winpath() { if command -v cygpath >/dev/null; then cygpath -w "$1" | sed 's/\\/\\\\/g'; else printf '%s' "$1"; fi; }
json() { printf '{"cwd":"%s","tool_name":"%s","tool_input":{%s}}' "$(winpath "$1")" "$2" "$3"; }
# Claude Code sets CLAUDE_PROJECT_DIR (a native path); the shim then finds the project without reading the JSON.
nativepath() { if command -v cygpath >/dev/null; then cygpath -w "$1"; else printf '%s' "$1"; fi; }
checkenv() { # $1=name $2=expected exit $3=project dir $4=json
  printf '%s' "$4" | CLAUDE_PROJECT_DIR="$(nativepath "$3")" "$SHIM" pre-tool-use >/dev/null 2>"$WORK/err"; got=$?
  if [ "$got" = "$2" ]; then pass=$((pass+1)); r=ok; else fail=$((fail+1)); r="FAIL ($(head -c 400 "$WORK/err"))"; fi
  printf '%-44s expected=%s got=%s %s\n' "$1" "$2" "$got" "$r"
}
fp() { printf '"file_path":"%s"' "$(winpath "$1")"; }
# The SHA-256 lockflight link records beside the engine; $1=engine $2=project.
sha() { if command -v sha256sum >/dev/null 2>&1; then sha256sum < "$1"; else shasum -a 256 < "$1"; fi | cut -d' ' -f1; }
record() { sha "$1" > "$2/.lockflight/bin/lockflight.sha256"; }
# A task being marked completed (TaskCompleted); $1=name $2=expected exit $3=dir [$4=text stderr must contain].
checktask() {
  printf '{"cwd":"%s","hook_event_name":"TaskCompleted","session_id":"probe","task_id":"t1","task_subject":"Probe task"}' "$(winpath "$3")" \
    | "$SHIM" task-completed >/dev/null 2>"$WORK/err"; got=$?
  if [ "$got" = "$2" ] && { [ -z "${4:-}" ] || grep -qF -- "$4" "$WORK/err"; }; then pass=$((pass+1)); r=ok; else fail=$((fail+1)); r="FAIL ($(head -c 400 "$WORK/err"))"; fi
  printf '%-44s expected=%s got=%s %s\n' "$1" "$2" "$got" "$r"
}
# A typed slash command (UserPromptExpansion); $1=dir $2=command name $3=arguments.
expansion() { printf '{"cwd":"%s","hook_event_name":"UserPromptExpansion","expansion_type":"slash_command","command_name":"%s","command_args":"%s","command_source":"plugin"}' "$(winpath "$1")" "$2" "$3"; }
checkexp() { # $1=name $2=expected exit $3=entrypoint $4=attended $5=json [$6=text stderr must contain]
  printf '%s' "$5" | CLAUDE_CODE_ENTRYPOINT="$3" CLAUDE_CODE_SESSION_ATTENDED="$4" "$SHIM" user-prompt-expansion >/dev/null 2>"$WORK/err"; got=$?
  if [ "$got" = "$2" ] && { [ -z "${6:-}" ] || grep -qF -- "$6" "$WORK/err"; }; then pass=$((pass+1)); r=ok; else fail=$((fail+1)); r="FAIL ($(head -c 400 "$WORK/err"))"; fi
  printf '%-44s expected=%s got=%s %s\n' "$1" "$2" "$got" "$r"
}

plain="$WORK/plain"; mkdir -p "$plain"
proj="$WORK/proj"; mkdir -p "$proj/src"
printf 'protected:\n  - "secrets/**"\n  - "lockflight.yaml"\n' > "$proj/lockflight.yaml"
version=$("$ENGINE" version)
printf '{"version":1,"isRoot":true,"tools":{"lockflight.cli":{"version":"%s","commands":["lockflight"]}}}' "$version" > "$proj/dotnet-tools.json"

check "no manifest: Write"                0 "$plain" "$(json "$plain" Write "$(fp "$plain/a")")"
checktask "no manifest: task completed"    0 "$plain"
checktask "not restored: task completed"   0 "$proj"
check "not restored: Write"               2 "$proj"  "$(json "$proj" Write "$(fp "$proj/src/a.cs")")"
check "not restored: Read"                0 "$proj"  "$(json "$proj" Read "$(fp "$proj/src/a.cs")")"
check "not restored: Bash restore+link"   0 "$proj"  "$(json "$proj" Bash "\"command\":\"dotnet tool restore && dotnet lockflight link\"")"
check "not restored: Bash other"          2 "$proj"  "$(json "$proj" Bash "\"command\":\"rm -rf src\"")"
check "not restored: Bash restore; chained" 2 "$proj" "$(json "$proj" Bash "\"command\":\"dotnet tool restore; rm -rf src\"")"
check "not restored: nested tool_name Read"  2 "$proj" '{"cwd":"'"$(winpath "$proj")"'","tool_name":"Bash","x":{"tool_name":"Read"}}'
check "not restored: nested Read before"      2 "$proj" '{"x":{"tool_name":"Read"},"cwd":"'"$(winpath "$proj")"'","tool_name":"Bash"}'
check "not restored: restore in another key"  2 "$proj" '{"cwd":"'"$(winpath "$proj")"'","tool_name":"Bash","tool_input":{"command":"rm -rf src"},"y":{"command":"dotnet tool restore"}}'
check "not restored: tool_name inside a string" 2 "$proj" '{"cwd":"'"$(winpath "$proj")"'","tool_name":"Bash","tool_input":{"description":"\"tool_name\":\"Read\"","command":"rm x"}}'
checkexp "no manifest: approve expansion"     0 cli 1 "$(expansion "$plain" lockflight:approve spec)"
checkexp "not restored: approve expansion"   2 cli 1 "$(expansion "$proj" lockflight:approve spec)" "nothing was approved"
checkexp "not restored: approve, other case"  2 cli 1 "$(expansion "$proj" LockFlight:Approve spec)" "nothing was approved"
checkexp "not restored: other slash command" 0 cli 1 "$(expansion "$proj" lockflight:status "")"
checkexp "not restored: baseline expansion"   2 cli 1 "$(expansion "$proj" lockflight:baseline tests)" "nothing was accepted"
checkexp "not restored: reset expansion"      2 cli 1 "$(expansion "$proj" lockflight:reset "")" "nothing was reset"
checkexp "not restored: amend, other case"    2 cli 1 "$(expansion "$proj" LockFlight:Amend "A.B --reason x")" "nothing was amended"
checkenv "env dir, no manifest: Write"     0 "$plain" "$(json "$plain" Write "$(fp "$plain/a")")"
checkenv "env dir, not restored: Write"    2 "$proj"  "$(json "$proj" Write "$(fp "$proj/src/a.cs")")"
checkenv "env dir, not restored: Read"     0 "$proj"  "$(json "$proj" Read "$(fp "$proj/src/a.cs")")"
cp "$proj/dotnet-tools.json" "$WORK/pin"
for dev in "$version-x.dev.1" 9.9.9-dev.1; do
  printf '{"version":1,"isRoot":true,"tools":{"lockflight.cli":{"version":"%s","commands":["lockflight"]}}}' "$dev" > "$proj/dotnet-tools.json"
  check "not restored, dev pin $dev: Write" 2 "$proj" "$(json "$proj" Write "$(fp "$proj/src/a.cs")")"
  if grep -q 'lockflight update' "$WORK/err"; then pass=$((pass+1)); echo "not restored, dev pin: names update: ok"; else fail=$((fail+1)); echo "not restored, dev pin: names update: FAIL ($(head -c 400 "$WORK/err"))"; fi
done
mv "$WORK/pin" "$proj/dotnet-tools.json"

mkdir -p "$proj/.lockflight/bin"; case "$ENGINE" in *.exe) cp "$ENGINE" "$proj/.lockflight/bin/lockflight.exe" ;; *) cp "$ENGINE" "$proj/.lockflight/bin/lockflight" ;; esac
# A blocked call cannot tell "the guard said no" from "the engine failed", so an engine that cannot run on its own (e.g. a
# framework-dependent build without its .dll) would make every block case pass. Stop instead.
linked=$(ls "$proj/.lockflight/bin/"lockflight*)
"$linked" version > /dev/null 2>&1 || { echo "the engine does not run as a single file: $ENGINE (use a Native AOT or single-file build)"; exit 1; }
# An engine without the hash record link writes (linked by a Lockflight before 0.2.0-alpha.7, or copied by hand) is not run, as
# with a record that is empty: reads pass, writes and a typed approval are blocked, and the person is told to run update.
for empty in missing empty; do
  if [ "$empty" = missing ]; then rm -f "$proj/.lockflight/bin/lockflight.sha256"; else : > "$proj/.lockflight/bin/lockflight.sha256"; fi
  check "unrecorded engine ($empty): Write blocked" 2 "$proj" "$(json "$proj" Write "$(fp "$proj/src/a.cs")")"
  if grep -q 'no hash record' "$WORK/err" && grep -q 'separate terminal window.*lockflight update' "$WORK/err"; then pass=$((pass+1)); echo "unrecorded engine ($empty): names update: ok"; else fail=$((fail+1)); echo "unrecorded engine ($empty): names update: FAIL ($(head -c 400 "$WORK/err"))"; fi
  check "unrecorded engine ($empty): Read allowed" 0 "$proj" "$(json "$proj" Read "$(fp "$proj/src/a.cs")")"
done
checkexp "unrecorded engine: approve blocked"  2 cli 1 "$(expansion "$proj" lockflight:approve spec)" "Nothing was approved"
if printf '{}' | CLAUDE_PROJECT_DIR="$(nativepath "$proj")" "$SHIM" session-start 2>/dev/null | grep -q '"systemMessage":"Lockflight did not run.*lockflight update'; then
  pass=$((pass+1)); echo "unrecorded engine: session notice names update: ok"
else
  fail=$((fail+1)); echo "unrecorded engine: session notice names update: FAIL"
fi
record "$linked" "$proj"
checkenv "env dir, linked: Write unprotected" 0 "$proj"  "$(json "$proj" Write "$(fp "$proj/src/a.cs")")"
checkenv "env dir, linked: Write protected"  2 "$proj"  "$(json "$proj" Write "$(fp "$proj/secrets/k")")"
checkenv "env subdir, linked: Write protected" 2 "$proj/src" "$(json "$proj/src" Write "$(fp "$proj/secrets/k")")"
check "linked: Write unprotected"         0 "$proj"  "$(json "$proj" Write "$(fp "$proj/src/a.cs")")"
check "linked: Write protected"           2 "$proj"  "$(json "$proj" Write "$(fp "$proj/secrets/k")")"
check "linked: Edit manifest (protected)" 2 "$proj"  "$(json "$proj" Edit "$(fp "$proj/lockflight.yaml")")"
check "linked: subdir cwd, protected"     2 "$proj"  "$(json "$proj/src" Write "$(fp "$proj/secrets/k")")"
case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*)
    check "linked: NTFS stream on protected file"  2 "$proj" "$(json "$proj" Write "\"file_path\":\"$(winpath "$proj/secrets/k"):hidden\"")"
    check "linked: trailing dot on protected dir"  2 "$proj" "$(json "$proj" Write "$(fp "$proj/secrets./k")")" ;;
  *)
    mkdir -p "$proj/secrets" && ln -s "$proj/secrets" "$proj/innocent"
    check "linked: write via symlinked dir"        2 "$proj" "$(json "$proj" Write "$(fp "$proj/innocent/k")")" ;;
esac
mkdir -p "$proj/specs/001-probe" && printf -- '### User Story 1 - Probe\n\n1. **Given** a probe, **When** it runs, **Then** it reports\n\n- **FR-001**: Probe\n' > "$proj/specs/001-probe/spec.md"
checkexp "linked: other slash command ignored"  0 sdk-cli 0 "$(expansion "$proj" lockflight:status "")"
checkexp "linked: approve, headless refused"    2 sdk-cli 0 "$(expansion "$proj" lockflight:approve spec)" "nothing was approved"
checkexp "linked: approve, unattended refused"  2 cli 0 "$(expansion "$proj" lockflight:approve spec)" "nothing was approved"
checkexp "linked: approve, attended shows code" 2 cli 1 "$(expansion "$proj" lockflight:approve spec)" "/lockflight:approve spec "
checkexp "linked: approve, wrong code refused"  2 cli 1 "$(expansion "$proj" lockflight:approve "spec zzzzzz")" "not the code shown"
checkexp "linked: approve alone, code waiting"  2 cli 1 "$(expansion "$proj" lockflight:approve "")" "Step 2 of 2 — type: /lockflight:approve spec"
checkexp "linked: baseline, headless refused"   2 sdk-cli 0 "$(expansion "$proj" lockflight:baseline build)" "nothing was accepted"
checkexp "linked: baseline, unknown kind"       2 cli 1 "$(expansion "$proj" lockflight:baseline everything)" "lockflight baseline build|tests|deps"
checkexp "linked: reset, unattended refused"    2 cli 0 "$(expansion "$proj" lockflight:reset "")" "nothing was reset"
checkexp "linked: reset, no evidence"           2 cli 1 "$(expansion "$proj" lockflight:reset "")" "no red/green evidence to discard"
checkexp "linked: amend, headless refused"      2 sdk-cli 0 "$(expansion "$proj" lockflight:amend "A.B --reason x")" "nothing was amended"
checkexp "linked: amend, no evidence"           2 cli 1 "$(expansion "$proj" lockflight:amend "A.B --reason x")" "no red/green evidence"
check "linked: CronCreate with /lockflight:reset" 2 "$proj" "$(json "$proj" CronCreate "\"prompt\":\"/lockflight:reset k7m2qx\"")"
check "linked: malformed JSON"            2 "$proj"  '{"cwd":"'"$proj"'","tool_name":'
checktask "linked, stop gate off: task completed" 0 "$proj"
# With the stop gate on, a task is completed only after the gate passed for the tree; nothing has passed here.
gated="$WORK/gated"; mkdir -p "$gated/.lockflight/bin"
printf 'stop:\n  gate: true\n' > "$gated/lockflight.yaml"; cp "$proj/dotnet-tools.json" "$gated/"
cp "$linked" "$proj/.lockflight/bin/lockflight.sha256" "$gated/.lockflight/bin/"
printf '.lockflight/\n' > "$gated/.gitignore"
git -C "$gated" init -q && git -C "$gated" add -A && git -C "$gated" -c user.email=p@p -c user.name=p commit -qm probe
checktask "linked, stop gate on: task blocked" 2 "$gated" "Probe task"
# Shell writes, checked while the pin still matches (after the pin changes below, every call is blocked anyway).
check "linked: bash redirect into protected" 2 "$proj" "$(json "$proj" Bash "\"command\":\"echo x > secrets/k\"")"
check "linked: git checkout into protected" 2 "$proj" "$(json "$proj" Bash "\"command\":\"git checkout HEAD -- secrets/k\"")"
check "linked: perl -pi on protected"       2 "$proj" "$(json "$proj" Bash "\"command\":\"perl -pi -e s/a/b/ secrets/k\"")"
check "linked: cd, then redirect into protected" 2 "$proj" "$(json "$proj" Bash "\"command\":\"cd secrets && echo x > k\"")"
check "linked: cd, then redirect elsewhere" 0 "$proj" "$(json "$proj" Bash "\"command\":\"cd src && echo x > k\"")"
check "linked: subshell cd, then rm protected" 2 "$proj" "$(json "$proj" Bash "\"command\":\"(cd /tmp); rm secrets/k\"")"
check "linked: piped cd, then rm protected"    2 "$proj" "$(json "$proj" Bash "\"command\":\"cd /tmp | true; rm secrets/k\"")"
check "linked: cd \$(pwd), then rm protected"  2 "$proj" "$(json "$proj" Bash "\"command\":\"cd \$(pwd) && rm secrets/k\"")"
check "linked: git checkout of a protected dir" 2 "$proj" "$(json "$proj" Bash "\"command\":\"git checkout HEAD -- secrets\"")"
check "linked: global.json is protected by default" 2 "$proj" "$(json "$proj" Edit "$(fp "$proj/global.json")")"
check "linked: newline, then rm protected"   2 "$proj" "$(json "$proj" Bash "\"command\":\"echo ok\\nrm secrets/k\"")"
check "linked: 2>> into protected"            2 "$proj" "$(json "$proj" Bash "\"command\":\"dotnet build 2>> secrets/k\"")"
check "linked: cp into a protected dir"       2 "$proj" "$(json "$proj" Bash "\"command\":\"cp /tmp/x secrets/\"")"
check "linked: sed -Ei on protected"          2 "$proj" "$(json "$proj" Bash "\"command\":\"sed -Ei 's/a/b/' secrets/k\"")"
check "linked: script runs rm on protected"   2 "$proj" "$(json "$proj" Bash "\"command\":\"script -q /dev/null rm secrets/k\"")"
check "linked: parallel rm on protected"      2 "$proj" "$(json "$proj" Bash "\"command\":\"parallel rm ::: src/a.cs secrets/k\"")"
check "linked: watch rm on protected"         2 "$proj" "$(json "$proj" Bash "\"command\":\"watch -n 1 rm secrets/k\"")"
check "linked: Write of scheduled tasks"      2 "$proj" "$(json "$proj" Write "$(fp "$proj/.claude/scheduled_tasks.json")")"
check "linked: rm -rf *"                      2 "$proj" "$(json "$proj" Bash "\"command\":\"rm -rf *\"")"
check "linked: PowerShell -Path: protected"   2 "$proj" "$(json "$proj" PowerShell "\"command\":\"Set-Content -Path:secrets/k -Value x\"")"
check "linked: unresolvable cd, then write"   2 "$proj" "$(json "$proj" Bash "\"command\":\"cd \$X && echo x > notes.txt\"")"
check "linked: ordinary build allowed"        0 "$proj" "$(json "$proj" Bash "\"command\":\"cd src && dotnet build 2>&1 | tee build.log\"")"
check "linked: rm \$(echo protected)"       2 "$proj" "$(json "$proj" Bash "\"command\":\"rm \$(echo secrets/k)\"")"
check "linked: PowerShell Join-Path target"   2 "$proj" "$(json "$proj" PowerShell "\"command\":\"Remove-Item (Join-Path secrets k)\"")"
check "linked: find -name *.tmp -delete"      0 "$proj" "$(json "$proj" Bash "\"command\":\"find . -type f -name '*.tmp' -delete\"")"
check "linked: computed log name allowed"    0 "$proj" "$(json "$proj" Bash "\"command\":\"touch build-\$(date +%s).log\"")"
check "linked: rm -rf .. (contains project)" 2 "$proj" "$(json "$proj" Bash "\"command\":\"rm -rf ..\"")"
check "linked: rm -rf unrelated outside path" 0 "$proj" "$(json "$proj" Bash "\"command\":\"rm -rf /tmp/lockflight-probe-unrelated\"")"
check "linked: computed name in protected dir" 2 "$proj" "$(json "$proj" Bash "\"command\":\"echo x > secrets/\$(date).log\"")"

sed "s/\"$version\"/\"9.9.9\"/" "$proj/dotnet-tools.json" > "$WORK/t" && mv "$WORK/t" "$proj/dotnet-tools.json"
touch "$proj/.lockflight/lockdown"
check "lockdown: write blocked"           2 "$proj"  "$(json "$proj" Write "$(fp "$proj/src/a.cs")")"
check "lockdown: read allowed"            0 "$proj"  "$(json "$proj" Read "$(fp "$proj/src/a.cs")")"
rm "$proj/.lockflight/lockdown"
check "pin mismatch: Write blocked"       2 "$proj"  "$(json "$proj" Write "$(fp "$proj/src/a.cs")")"
check "pin mismatch: Read allowed"        0 "$proj"  "$(json "$proj" Read "$(fp "$proj/src/a.cs")")"
check "pin mismatch: restore+link allowed" 0 "$proj" "$(json "$proj" Bash "\"command\":\"dotnet tool restore && dotnet lockflight link\"")"
check "pin mismatch: other Bash blocked"  2 "$proj"  "$(json "$proj" Bash "\"command\":\"dotnet build\"")"
# A plugin that needs a newer engine than the project runs tells the person at session start.
mkdir -p "$WORK/plugin/bin" && cp "$SHIM" "$WORK/plugin/bin/" && printf '999.0.0\n' > "$WORK/plugin/min-engine-version"
if printf '%s' "$(json "$proj" Read "$(fp "$proj/src/a.cs")")" | "$WORK/plugin/bin/lockflight-hook" session-start 2>/dev/null | grep -q 'needs engine 999.0.0'; then
  pass=$((pass+1)); echo "plugin needs newer engine: session notice: ok"
else
  fail=$((fail+1)); echo "plugin needs newer engine: session notice: FAIL"
fi
# The session-start hook hands the loaded plugin's requirement and directory to the session's later Bash commands (doctor).
: > "$WORK/env-file"
printf '%s' "$(json "$proj" Read "$(fp "$proj/src/a.cs")")" | CLAUDE_ENV_FILE="$WORK/env-file" CLAUDE_PLUGIN_ROOT="$WORK/plugin" "$WORK/plugin/bin/lockflight-hook" session-start > /dev/null 2>&1
if grep -qx "export LOCKFLIGHT_PLUGIN_MIN_ENGINE='999.0.0'" "$WORK/env-file" && grep -qx "export LOCKFLIGHT_PLUGIN_ROOT='$WORK/plugin'" "$WORK/env-file"; then
  pass=$((pass+1)); echo "session start: exports the loaded plugin: ok"
else
  fail=$((fail+1)); echo "session start: exports the loaded plugin: FAIL ($(head -c 400 "$WORK/env-file"))"
fi
sed 's/"9.9.9"/"9.9.9-dev.1"/' "$proj/dotnet-tools.json" > "$WORK/t" && mv "$WORK/t" "$proj/dotnet-tools.json"
check "dev pin mismatch: Write blocked"   2 "$proj"  "$(json "$proj" Write "$(fp "$proj/src/a.cs")")"
if grep -q 'lockflight update' "$WORK/err"; then pass=$((pass+1)); echo "dev pin mismatch: names update: ok"; else fail=$((fail+1)); echo "dev pin mismatch: names update: FAIL ($(head -c 400 "$WORK/err"))"; fi
# An engine committed to the repository (a cloned project could ship any program there) is never run.
cloned="$WORK/cloned"; mkdir -p "$cloned/.lockflight/bin" "$cloned/src"
cp "$proj/lockflight.yaml" "$cloned/"
printf '{"version":1,"isRoot":true,"tools":{"lockflight.cli":{"version":"%s","commands":["lockflight"]}}}' "$version" > "$cloned/dotnet-tools.json"
committed="$cloned/.lockflight/bin/lockflight"; case "$ENGINE" in *.exe) committed="$committed.exe" ;; esac
cp "$ENGINE" "$committed"
git -C "$cloned" init -q && git -C "$cloned" add -f "${committed#"$cloned"/}"
check "committed engine: Write blocked"     2 "$cloned" "$(json "$cloned" Write "$(fp "$cloned/src/a.cs")")"
if grep -q 'committed to the repository' "$WORK/err"; then pass=$((pass+1)); echo "committed engine: names the reason: ok"; else fail=$((fail+1)); echo "committed engine: names the reason: FAIL ($(head -c 400 "$WORK/err"))"; fi
check "committed engine: Read allowed"      0 "$cloned" "$(json "$cloned" Read "$(fp "$cloned/src/a.cs")")"
checkexp "committed engine: approve blocked" 2 cli 1 "$(expansion "$cloned" lockflight:approve spec)" "Nothing was approved"
checkexp "committed engine: amend blocked"   2 cli 1 "$(expansion "$cloned" lockflight:amend "A.B --reason x")" "Nothing was amended"
git -C "$cloned" rm -q --cached "${committed#"$cloned"/}"
record "$committed" "$cloned"
check "untracked engine: Write allowed"     0 "$cloned" "$(json "$cloned" Write "$(fp "$cloned/src/a.cs")")"
# A passed check is remembered until the engine file changes; an engine committed later is caught again.
touch -t 200001010000 "$cloned/.git/lockflight-engine-checked"
cp -f "$ENGINE" "$committed" && git -C "$cloned" add -f "${committed#"$cloned"/}"
check "engine committed later: Write blocked" 2 "$cloned" "$(json "$cloned" Write "$(fp "$cloned/src/a.cs")")"
# link records the engine's SHA-256 beside it; an engine that no longer matches its record is not run.
git -C "$cloned" rm -q --cached "${committed#"$cloned"/}"
record "$committed" "$cloned"
check "recorded engine: Write allowed"       0 "$cloned" "$(json "$cloned" Write "$(fp "$cloned/src/a.cs")")"
# The engine replaced within the same second as the last passed check, the marker not dated back: some shells compare -nt to
# the whole second, so the shim must not take a marker from that second as newer than the engine. Starts at a fresh second so
# the pass, its unchanged twin and the replacement all land in one second; no marker, so the pass writes one in that second.
second=$(date +%s); while [ "$(date +%s)" = "$second" ]; do :; done
rm -f "$cloned/.git/lockflight-engine-checked"
check "same second: passed check"              0 "$cloned" "$(json "$cloned" Write "$(fp "$cloned/src/a.cs")")"
check "same second: unchanged engine allowed"  0 "$cloned" "$(json "$cloned" Write "$(fp "$cloned/src/a.cs")")"
cp -f "$committed" "$WORK/engine-linked" && chmod u+w "$committed" && printf 'replaced' >> "$committed"
check "same second: replaced engine blocked"   2 "$cloned" "$(json "$cloned" Write "$(fp "$cloned/src/a.cs")")"
[ "$(date +%s)" = "$((second + 1))" ] || echo "same second: note: the cases above crossed a second boundary"
cp -f "$WORK/engine-linked" "$committed"
# The engine itself replaced after link recorded it, the record left as it was: not run, so writes are refused. The last passed
# check is dated back, as it is when the engine is replaced later: the shim's -nt compares whole seconds on some shells.
cp -f "$committed" "$WORK/engine-linked" && touch -t 200001010000 "$cloned/.git/lockflight-engine-checked" && chmod u+w "$committed" && printf 'replaced' >> "$committed"
check "engine replaced after link: Write blocked" 2 "$cloned" "$(json "$cloned" Write "$(fp "$cloned/src/a.cs")")"
check "engine replaced after link: Read allowed"  0 "$cloned" "$(json "$cloned" Read "$(fp "$cloned/src/a.cs")")"
cp -f "$WORK/engine-linked" "$committed"
check "engine restored: Write allowed"        0 "$cloned" "$(json "$cloned" Write "$(fp "$cloned/src/a.cs")")"
# A replacement that keeps an older modification time: moved into place (a new file) or copied over the engine with cp -p (the
# same file, written in place). Each twin puts back the linked engine the same way and must still run.
cp "$WORK/engine-linked" "$WORK/older" && touch -t 200001010000 "$WORK/older" && mv -f "$WORK/older" "$committed"
check "older engine moved in: twin allowed"   0 "$cloned" "$(json "$cloned" Write "$(fp "$cloned/src/a.cs")")"
cp "$WORK/engine-linked" "$WORK/older" && chmod u+w "$WORK/older" && printf 'replaced' >> "$WORK/older" && touch -t 200001010000 "$WORK/older"
mv -f "$WORK/older" "$committed"
check "older engine moved in: Write blocked"  2 "$cloned" "$(json "$cloned" Write "$(fp "$cloned/src/a.cs")")"
cp -f "$WORK/engine-linked" "$committed"
check "engine moved back: Write allowed"      0 "$cloned" "$(json "$cloned" Write "$(fp "$cloned/src/a.cs")")"
# The pass is remembered with a hard link to the checked engine; without one every call would hash it again.
if [ "$committed" -ef "$cloned/.git/lockflight-engine-checked.engine" ]; then pass=$((pass+1)); echo "passed check holds the engine: ok"; else fail=$((fail+1)); echo "passed check holds the engine: FAIL"; fi
# A passed check leaves the engine read-only, so a plain cp -p is refused and -f replaces the file instead of writing into it.
# Windows has no such check (a read-only engine could not be replaced there), so these two run elsewhere only.
case "$ENGINE" in
  *.exe) echo "older engine copied with cp -p: skipped on Windows" ;;
  *) cp "$WORK/engine-linked" "$WORK/older" && touch -t 200001010000 "$WORK/older"
     cp -p "$WORK/older" "$committed" 2>/dev/null || cp -pf "$WORK/older" "$committed"
     check "older engine copied with cp -p: twin allowed" 0 "$cloned" "$(json "$cloned" Write "$(fp "$cloned/src/a.cs")")"
     chmod u+w "$WORK/older" && printf 'replaced' >> "$WORK/older" && touch -t 200001010000 "$WORK/older"
     cp -p "$WORK/older" "$committed" 2>/dev/null || cp -pf "$WORK/older" "$committed"
     check "older engine copied with cp -p: Write blocked" 2 "$cloned" "$(json "$cloned" Write "$(fp "$cloned/src/a.cs")")"
     cp -f "$WORK/engine-linked" "$committed"
     check "engine copied back: Write allowed"   0 "$cloned" "$(json "$cloned" Write "$(fp "$cloned/src/a.cs")")" ;;
esac
# A record removed after a passed check: the remembered pass does not count without one.
mv "$cloned/.lockflight/bin/lockflight.sha256" "$WORK/record"
check "record removed after a pass: Write blocked" 2 "$cloned" "$(json "$cloned" Write "$(fp "$cloned/src/a.cs")")"
mv "$WORK/record" "$cloned/.lockflight/bin/lockflight.sha256"
check "record put back: Write allowed"        0 "$cloned" "$(json "$cloned" Write "$(fp "$cloned/src/a.cs")")"
printf '%064d\n' 0 > "$cloned/.lockflight/bin/lockflight.sha256"
touch -t 200001010000 "$cloned/.git/lockflight-engine-checked"
check "replaced engine: Write blocked"       2 "$cloned" "$(json "$cloned" Write "$(fp "$cloned/src/a.cs")")"
if grep -q 'SHA-256' "$WORK/err"; then pass=$((pass+1)); echo "replaced engine: names the reason: ok"; else fail=$((fail+1)); echo "replaced engine: names the reason: FAIL ($(head -c 400 "$WORK/err"))"; fi
check "replaced engine: Read allowed"        0 "$cloned" "$(json "$cloned" Read "$(fp "$cloned/src/a.cs")")"
checkexp "replaced engine: approve blocked"  2 cli 1 "$(expansion "$cloned" lockflight:approve spec)" "Nothing was approved"

# An engine that crashes must block, never allow. On Windows the shim prefers lockflight.exe, so the stub there is a real .exe
# (Git Bash's false.exe, exit 1); elsewhere a script exiting 3. A passed check left the engine read-only, so each stand-in below
# replaces the file, and each is recorded as link would record it.
case "$ENGINE" in
  *.exe) cp /usr/bin/false.exe "$proj/.lockflight/bin/lockflight.exe" || { echo "no /usr/bin/false.exe to stand in for a crashing engine"; exit 1; }; crash=1 ;;
  *) rm -f "$proj/.lockflight/bin/lockflight"; printf '#!/bin/sh\nexit 3\n' > "$proj/.lockflight/bin/lockflight"; chmod +x "$proj/.lockflight/bin/lockflight"; crash=3 ;;
esac
crashing="$proj/.lockflight/bin/lockflight"; [ ! -f "$crashing.exe" ] || crashing="$crashing.exe"; record "$crashing" "$proj"
check "crashing engine: tool call blocked" 2 "$proj" "$(json "$proj" Write "$(fp "$proj/src/a.cs")")"
checktask "crashing engine: task not blocked" "$crash" "$proj"
checkexp "crashing engine: approve blocked"  2 cli 1 "$(expansion "$proj" lockflight:approve spec)" "nothing was approved"
checkexp "crashing engine: baseline blocked" 2 cli 1 "$(expansion "$proj" lockflight:baseline build)" "nothing was accepted"
checkexp "crashing engine: reset blocked"    2 cli 1 "$(expansion "$proj" lockflight:reset "")" "nothing was reset"
checkexp "crashing engine: other slash command" "$crash" cli 1 "$(expansion "$proj" lockflight:status "")"

# An engine older than the plugin refuses the event it does not know; that must not block every task completion.
case "$ENGINE" in
  *.exe) ;;
  *) rm -f "$proj/.lockflight/bin/lockflight"
     printf '#!/bin/sh\ncat >/dev/null\necho "lockflight: this engine does not know the hook event it was called for; update it with: x" >&2\nexit 2\n' > "$proj/.lockflight/bin/lockflight"
     chmod +x "$proj/.lockflight/bin/lockflight" && record "$proj/.lockflight/bin/lockflight" "$proj"
     checktask "older engine: unknown event let through" 0 "$proj" ;;
esac

echo "passed=$pass failed=$fail"; rm -rf "$WORK"; [ "$fail" = 0 ]
