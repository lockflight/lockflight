#!/usr/bin/env bash
# The crew's trust boundary (the judge gives facts and verdicts; crew gives advice). The lockflight-crew plugin is
# released on its own (crew/v* tags) and installed beside the judge, so it must never ship what makes or bends a verdict:
#   - no hooks: no hooks/ directory, no "hooks" in plugin.json, no hooks: in an agent's or skill's frontmatter;
#   - no executables, servers or settings: no bin/, no MCP or LSP servers, no settings.json, no output styles;
#   - no guard or policy files: lockflight.yaml, .lockflight/, engine.lock, CODEOWNERS, the Spec Kit preset, a schema;
#   - no verdicts: its agents and skills name no verdict record (approvals, red/green evidence, review findings, results,
#     baselines) and no command that records a person's decision or the review of record (approve other than approve verify,
#     baseline, redgreen reset or amend, review, update, engine install).
# Evals (evals/) are test material: they are checked for files, not for text. Run from anywhere in the repository; for tests,
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

# Frontmatter is the block between the first two '---' lines.
while IFS= read -r -d '' file; do
  if awk 'NR == 1 && $0 != "---" { exit 1 } NR > 1 && $0 == "---" { exit 1 } NR > 1 && /^hooks:/ { found = 1; exit 0 } END { exit !found }' "$file"; then
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
  # The engine refuses these to the agent anyway (human-only commands); the crew must not even suggest running them.
  decisions='lockflight (approve($|[^ ]| [^v]| v[^e])|baseline|redgreen (reset|amend)|review( |$|`)|update|engine install)'
  if matches=$(grep -rnE "$decisions" "${text_dirs[@]}"); then
    fail "names a command that records a decision or the review of record:"$'\n'"$matches"
  fi
fi

if [ "$problems" -gt 0 ]; then
  echo "crew-trust-boundary: $problems problem(s) in $crew" >&2
  exit 1
fi
echo "crew-trust-boundary: ok, $crew ships advice only (agents and skills, no hooks, policy or verdicts)"
