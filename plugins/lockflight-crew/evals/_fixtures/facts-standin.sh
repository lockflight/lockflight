#!/usr/bin/env bash
# Stand-in for the Lockflight engine in the advice-skill evals: `facts --json` prints the document the real engine printed for
# the case's fixture (saved by the scaffold as .lockflight/facts.json); a human-only command is refused as the real engine
# refuses it inside a Claude Code session; anything else says it is a stand-in.
set -u
root=$(cd "$(dirname "$0")/../.." && pwd)
case "${1:-} ${2:-}" in
  "facts --json") cat "$root/.lockflight/facts.json"; exit 0 ;;
  "facts ") echo "facts (eval stand-in); --json for the full document"; exit 0 ;;
  "approve "*|"baseline "*|"redgreen reset"|"redgreen amend"|"review dismiss"|"update "*)
    echo "lockflight: '$1 ${2:-}' records a human decision and is refused inside a Claude Code session; ask the user to run it in a separate terminal window" >&2
    exit 2 ;;
esac
echo "lockflight (eval stand-in): '$*' is not available in this fixture" >&2
exit 3
