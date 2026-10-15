#!/bin/sh
# Team hook: format the edited C# file (rewrites files outside any PreToolUse guard).
input=$(cat)
file=$(printf '%s' "$input" | sed -n 's/.*"file_path":"\([^"]*\.cs\)".*/\1/p')
[ -n "$file" ] && command -v dotnet >/dev/null && dotnet format whitespace --folder --include "$file" "$(dirname "$file")" >/dev/null 2>&1
exit 0
