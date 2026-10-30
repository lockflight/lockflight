#!/usr/bin/env bash
# The crew's trust boundary (the judge gives facts and verdicts; crew gives advice). The lockflight-crew plugin is
# released on its own (crew/v* tags) and installed beside the judge, so it must never ship what makes or bends a verdict:
#   - no hooks: no hooks/ directory, no "hooks" in plugin.json, no hooks: in an agent's or skill's frontmatter;
#   - no executables, servers or settings: no bin/, no executable file outside evals/, no MCP or LSP servers, no settings.json,
#     no output styles;
#   - no guard or policy files: lockflight.yaml, .lockflight/, engine.lock, CODEOWNERS, the Spec Kit preset, a schema;
#   - no verdicts: its agents and skills name no verdict record (approvals, red/green evidence, review findings, results,
#     baselines) and no command that records a person's decision or the review of record: approve other than approve verify,
#     baseline, redgreen reset or amend, review other than review status, update, engine install, with or without global
#     options before the verb. The session commands (/lockflight:approve and the like) may be named: only a person can type
#     them, so the crew may tell the person which one to type.
# Evals (evals/) are test material: they are checked for files, not for text or executable scripts (their scaffolds run in the
# eval sandbox, never in a user's session). Run from anywhere in the repository; for tests,
# the first argument replaces the plugin directory.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
crew=${1:-plugins/lockflight-crew}
manifest="$crew/.claude-plugin/plugin.json"
problems=0

fail() { echo "crew-trust-boundary: $*" >&2; problems=$((problems + 1)); }

[ -f "$manifest" ] || { echo "crew-trust-boundary: $manifest not found" >&2; exit 1; }

for forbidden in hooks bin .mcp.json .lsp.json settings.json output-styles speckit-preset; do
  [ ! -e "$crew/$forbidden" ] || fail "$crew/$forbidden: the crew ships no hooks, executables, servers, settings or presets"
done

python3 -I - "$manifest" <<'PY' || problems=$((problems + 1))
import json, sys
path = sys.argv[1]
keys = sorted(set(json.load(open(path, encoding="utf-8"))) & {"hooks", "mcpServers", "lspServers", "outputStyles", "commands"})
if keys:
    print(f"crew-trust-boundary: {path} declares {', '.join(keys)}: the crew ships agents and skills only", file=sys.stderr)
    sys.exit(1)
PY

while IFS= read -r -d '' file; do
  fail "$file: a guard or policy file; the crew never carries one"
done < <(find "$crew" \( -name lockflight.yaml -o -name 'engine.lock' -o -name CODEOWNERS -o -name '*.schema.json' \
  -o -name .lockflight -o -name 'lockflight-hook' \) -print0)

while IFS= read -r -d '' file; do
  fail "$file: an executable file; the crew ships agents and skills (only evals/ may hold scripts)"
done < <(find "$crew" -path "$crew/evals" -prune -o -type f -perm -u+x -print0)

# Frontmatter is the block between the first two '---' lines (CRLF endings and a quoted key included).
while IFS= read -r -d '' file; do
  if awk '{ sub(/\r$/, "") } NR == 1 && $0 != "---" { exit 1 } NR > 1 && $0 == "---" { exit 1 } NR > 1 && /^["'"'"']?hooks["'"'"']?[[:space:]]*:/ { found = 1; exit 0 } END { exit !found }' "$file"; then
    fail "$file: declares hooks in its frontmatter; the crew runs no hooks"
  fi
done < <(find "$crew/agents" "$crew/skills" -name '*.md' -print0 2> /dev/null)

text_dirs=()
for directory in agents skills; do
  [ ! -d "$crew/$directory" ] || text_dirs+=("$crew/$directory")
done
if [ "${#text_dirs[@]}" -gt 0 ]; then
  verdict_records='approvals/|redgreen\.json|\.lockflight/(features|baselines|review)|lockflight\.(result|approval|blocked)/v'
  if matches=$(grep -rnE "$verdict_records" "${text_dirs[@]}"); then
    fail "names a verdict record; the crew gives advice and leaves records to the engine:"$'\n'"$matches"
  fi
  # The engine refuses most of these to the agent anyway (human-only commands); the crew must not even suggest running them.
  # Each command is matched on its own, then the allowed forms (approve verify, review status) are dropped.
  verbs='(approve|baseline|redgreen (reset|amend)|review|update|engine install)([^a-z-]|$)( *[a-z-]+)?'
  decisions="lockflight( --?[a-z-]+)* $verbs"
  if matches=$(grep -rnoE "$decisions" "${text_dirs[@]}" | grep -vE 'lockflight( --?[a-z-]+)* (approve[^a-z-]+verify|review[^a-z-]+status)$'); then
    fail "names a command that records a decision or the review of record:"$'\n'"$matches"
  fi
fi

if [ "$problems" -gt 0 ]; then
  echo "crew-trust-boundary: $problems problem(s) in $crew" >&2
  exit 1
fi
echo "crew-trust-boundary: ok, $crew ships advice only (agents and skills, no hooks, policy or verdicts)"
