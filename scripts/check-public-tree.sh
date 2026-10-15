#!/usr/bin/env bash
# Fails when a tracked file cites something a reader of this repository cannot open: design records, planning IDs or
# experiment IDs. Commits, comments and messages describe the change itself (CONTRIBUTING.md).
#   scripts/check-public-tree.sh [pathspec…]
# A maintainer can add private patterns, one extended regex per line, in the file named by LOCKFLIGHT_PRIVATE_PATTERNS.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

patterns=(
  'ADR[ -]?[0-9]{2,4}'
  'docs/(vision|roadmap)\.md|docs/(decisions|design|phases)/|research/[0-9]|spike/'
  '[Vv]ision §|[Rr]oadmap (block|slice)'
  '[Dd]ogfood'
  '(^|[^A-Za-z0-9#-])(F|W|E)[0-9]{1,2}[ab]?( probe|:| —)'
)
if [ -n "${LOCKFLIGHT_PRIVATE_PATTERNS:-}" ] && [ -f "$LOCKFLIGHT_PRIVATE_PATTERNS" ]; then
  while IFS= read -r line; do [ -n "$line" ] && patterns+=("$line"); done < "$LOCKFLIGHT_PRIVATE_PATTERNS"
fi

# Spec Kit's own skills and templates (vendored into the samples and the preset) use these words in their own sense.
excluded=(':!samples/*/.claude/skills/**' ':!samples/**/.specify/**' ':!scripts/check-public-tree.sh')

regex=$(IFS='|'; echo "${patterns[*]}")
if git grep -nIE "$regex" -- "${@:-.}" "${excluded[@]}"; then
  echo "check-public-tree: the lines above cite records outside this repository; describe the reason instead." >&2
  exit 1
fi
echo "check-public-tree: clean"
