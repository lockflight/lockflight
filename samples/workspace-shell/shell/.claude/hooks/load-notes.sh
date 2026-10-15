#!/bin/sh
# Team hook: print the workspace notes at session start.
[ -f "$CLAUDE_PROJECT_DIR/NOTES.md" ] && cat "$CLAUDE_PROJECT_DIR/NOTES.md"
exit 0
