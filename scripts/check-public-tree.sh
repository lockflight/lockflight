#!/usr/bin/env bash
# Fails when a tracked file, or a commit message on this branch, cites something a reader of this repository cannot open
# (design records, planning IDs or experiment IDs) or leaks something personal (a home directory path with a real account name,
# an email address other than a noreply or example one). Commits, comments and messages describe the change itself
# (CONTRIBUTING.md).
#   scripts/check-public-tree.sh [pathspec…]
# Commit messages are those in <base>..HEAD, where <base> is LOCKFLIGHT_PUBLIC_BASE or origin/main; they are skipped with a
# note when the base is not available (a shallow CI checkout). Pull request titles and bodies are not seen here.
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

# Examples use placeholder accounts (/home/u, /Users/x, /home/user, /home/runner); anything else may be a real one.
home_path='(/Users/|/home/)[A-Za-z0-9._-]+'
home_placeholder='^(/Users/|/home/)(u|x|me|you|user|runner|name|alice|bob|someone)$'
# Commit trailers and examples use noreply addresses and the reserved example domains.
email='[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}'
email_allowed='noreply|@example\.(com|org|net)$|^git@github\.com$'

# Spec Kit's own skills and templates (vendored into the samples and the preset) use these words in their own sense.
excluded=(':!samples/*/.claude/skills/**' ':!samples/**/.specify/**' ':!scripts/check-public-tree.sh')

regex=$(IFS='|'; echo "${patterns[*]}")
failed=0

# report <found> <what to do>: prints the lines found and marks the check failed.
report() {
  if [ -n "$1" ]; then
    printf '%s\n' "$1"
    echo "check-public-tree: $2" >&2
    failed=1
  fi
}

# disallowed <allowed regex> <matches>: the <prefix>:<match> lines (from grep -o) whose match is not allowed.
disallowed() { printf '%s\n' "$2" | allowed="$1" awk -F: 'NF > 1 && $NF !~ ENVIRON["allowed"]'; }

report "$(git grep -nIE "$regex" -- "${@:-.}" "${excluded[@]}" || true)" \
  "the lines above cite records outside this repository; describe the reason instead."
report "$(disallowed "$home_placeholder" "$(git grep -nIoE "$home_path" -- "${@:-.}" "${excluded[@]}" || true)")" \
  "the lines above contain a home directory path; use a placeholder such as /home/user."
report "$(disallowed "$email_allowed" "$(git grep -nIoE "$email" -- "${@:-.}" "${excluded[@]}" || true)")" \
  "the lines above contain an email address; use a noreply address or example.com."

base="${LOCKFLIGHT_PUBLIC_BASE:-origin/main}"
if git rev-parse -q --verify "$base^{commit}" >/dev/null; then
  messages=$(git log --format='%h %B' "$base..HEAD")
  if [ -n "$messages" ]; then
    report "$(printf '%s\n' "$messages" | grep -nE "$regex" || true)" \
      "commit messages in $base..HEAD cite the records above; reword them before pushing."
    report "$(disallowed "$home_placeholder" "$(printf '%s\n' "$messages" | grep -noE "$home_path" || true)")" \
      "commit messages in $base..HEAD contain the home directory paths above; reword them before pushing."
    report "$(disallowed "$email_allowed" "$(printf '%s\n' "$messages" | grep -noE "$email" || true)")" \
      "commit messages in $base..HEAD contain the email addresses above; reword them before pushing."
  fi
else
  echo "check-public-tree: $base is not available, so commit messages were not checked."
fi

[ "$failed" = 0 ] || exit 1
echo "check-public-tree: clean"
