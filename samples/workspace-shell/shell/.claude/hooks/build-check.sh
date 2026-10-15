#!/bin/sh
# Team hook: build the services at the end of a turn (runs in parallel with any other Stop hook).
for sln in "$CLAUDE_PROJECT_DIR"/services/*/*.sln; do
  [ -f "$sln" ] || continue
  dotnet build "$sln" -nologo -v q >/dev/null 2>&1 || { echo "Build failed: $sln" >&2; exit 2; }
done
exit 0
