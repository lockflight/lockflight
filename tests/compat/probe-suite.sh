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
checkexp "not restored: approve expansion"   0 cli 1 "$(expansion "$proj" lockflight:approve spec)"
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
mkdir -p "$proj/specs/001-probe" && printf -- '- **FR-001**: Probe\n' > "$proj/specs/001-probe/spec.md"
checkexp "linked: other slash command ignored"  0 sdk-cli 0 "$(expansion "$proj" lockflight:status "")"
checkexp "linked: approve, headless refused"    2 sdk-cli 0 "$(expansion "$proj" lockflight:approve spec)" "nothing was approved"
checkexp "linked: approve, unattended refused"  2 cli 0 "$(expansion "$proj" lockflight:approve spec)" "nothing was approved"
checkexp "linked: approve, attended shows code" 2 cli 1 "$(expansion "$proj" lockflight:approve spec)" "/lockflight:approve spec "
checkexp "linked: approve, wrong code refused"  2 cli 1 "$(expansion "$proj" lockflight:approve "spec zzzzzz")" "not the code shown"
check "linked: malformed JSON"            2 "$proj"  '{"cwd":"'"$proj"'","tool_name":'
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
sed 's/"9.9.9"/"9.9.9-dev.1"/' "$proj/dotnet-tools.json" > "$WORK/t" && mv "$WORK/t" "$proj/dotnet-tools.json"
check "dev pin mismatch: Write blocked"   2 "$proj"  "$(json "$proj" Write "$(fp "$proj/src/a.cs")")"
if grep -q 'lockflight update' "$WORK/err"; then pass=$((pass+1)); echo "dev pin mismatch: names update: ok"; else fail=$((fail+1)); echo "dev pin mismatch: names update: FAIL ($(head -c 400 "$WORK/err"))"; fi
case "$ENGINE" in
  *.exe) ;;
  *)
    printf '#!/bin/sh\nexit 3\n' > "$proj/.lockflight/bin/lockflight"
    check "crashing engine: tool call blocked" 2 "$proj" "$(json "$proj" Write "$(fp "$proj/src/a.cs")")" ;;
esac

echo "passed=$pass failed=$fail"; rm -rf "$WORK"; [ "$fail" = 0 ]
