#!/usr/bin/env bash
# scripts/crew-trust-boundary.sh on hand-made crew plugins: a clean one passes, and each thing that would let the crew make or
# bend a verdict (hooks, executables, servers, policy files, verdict records, decision commands) fails it.
set -euo pipefail

root=$(cd "$(dirname "$0")/../.." && pwd)
script="$root/scripts/crew-trust-boundary.sh"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
failures=0

ok() { echo "ok   $1"; }
bad() { echo "FAIL $1"; failures=$((failures + 1)); }

# crew <name>: a clean crew plugin at $work/<name>, printed.
crew() {
  local dir="$work/$1"
  mkdir -p "$dir/.claude-plugin" "$dir/agents" "$dir/skills/retro" "$dir/evals/case"
  printf '{ "name": "lockflight-crew", "version": "1.0.0" }\n' > "$dir/.claude-plugin/plugin.json"
  printf -- '---\nname: planner\ntools: Read, Write, Bash\n---\n\nRun `lockflight phase check tasks`. Check with `lockflight approve verify --feature <id>`.\n' > "$dir/agents/planner.md"
  printf -- '---\nname: retro\n---\n\nRead `lockflight facts --json`; never edit hooks yourself.\n' > "$dir/skills/retro/SKILL.md"
  printf 'The grader checks that the agent did not run lockflight approve spec.\n' > "$dir/evals/case/grader.md"
  echo "$dir"
}

expect() {
  local label="$1" want="$2" dir="$3"
  if (cd "$root" && bash "$script" "$dir") > "$work/out" 2>&1; then got=pass; else got=fail; fi
  if [ "$got" = "$want" ]; then ok "$label"; else bad "$label (expected $want, got $got)"; cat "$work/out"; fi
}

expect "a crew of agents and skills passes, evals included" pass "$(crew clean)"

dir=$(crew hooks-dir); mkdir "$dir/hooks"; echo '{}' > "$dir/hooks/hooks.json"
expect "a hooks directory fails" fail "$dir"

dir=$(crew hooks-key); printf '{ "name": "lockflight-crew", "version": "1.0.0", "hooks": "./h.json" }\n' > "$dir/.claude-plugin/plugin.json"
expect "hooks in plugin.json fail" fail "$dir"

dir=$(crew mcp); printf '{ "name": "lockflight-crew", "mcpServers": {} }\n' > "$dir/.claude-plugin/plugin.json"
expect "an MCP server in plugin.json fails" fail "$dir"

dir=$(crew frontmatter-hooks); printf -- '---\nname: x\nhooks:\n  Stop: []\n---\n' > "$dir/agents/x.md"
expect "hooks in an agent's frontmatter fail" fail "$dir"

dir=$(crew body-hooks); printf -- '---\nname: x\n---\n\nhooks: are the user'"'"'s\n' > "$dir/agents/x.md"
expect "the word hooks in an agent's body passes" pass "$dir"

dir=$(crew bin); mkdir "$dir/bin"; touch "$dir/bin/tool"
expect "an executable directory fails" fail "$dir"

dir=$(crew policy); touch "$dir/skills/retro/lockflight.yaml"
expect "a manifest anywhere in the crew fails" fail "$dir"

dir=$(crew lock); mkdir -p "$dir/evals/case/.lockflight"; touch "$dir/evals/case/.lockflight/engine.lock"
expect "an engine lock in an eval fixture fails" fail "$dir"

dir=$(crew record); printf 'Write the result to .lockflight/features/<id>/approvals/spec.json.\n' >> "$dir/agents/planner.md"
expect "an agent naming an approval record fails" fail "$dir"

dir=$(crew redgreen); printf 'Update redgreen.json when the tests fail.\n' >> "$dir/skills/retro/SKILL.md"
expect "a skill naming the red/green evidence fails" fail "$dir"

for command in "approve spec" "baseline tests" "redgreen reset" "redgreen amend" "review --feature x" "update" "engine install"; do
  dir=$(crew "decision-${command// /-}"); printf 'Then run `lockflight %s`.\n' "$command" >> "$dir/agents/planner.md"
  expect "an agent naming 'lockflight $command' fails" fail "$dir"
done

dir=$(crew global-option); printf 'Run `lockflight --verbose approve spec`.\n' >> "$dir/agents/planner.md"
expect "a decision command after a global option fails" fail "$dir"

dir=$(crew allowed); printf 'Check `lockflight review status`; lockflight updates the ledger; the person types `/lockflight:approve spec`.\n' >> "$dir/agents/planner.md"
expect "review status, prose and a session command for the person pass" pass "$dir"

dir=$(crew executable); touch "$dir/skills/retro/run.sh"; chmod +x "$dir/skills/retro/run.sh"; touch "$dir/evals/case/scaffold.sh"; chmod +x "$dir/evals/case/scaffold.sh"
expect "an executable outside evals fails" fail "$dir"

dir=$(crew eval-script); touch "$dir/evals/case/scaffold.sh"; chmod +x "$dir/evals/case/scaffold.sh"
expect "an executable scaffold in evals passes" pass "$dir"

dir=$(crew crlf); printf -- '---\r\nname: x\r\n"hooks":\r\n  Stop: []\r\n---\r\n' > "$dir/agents/x.md"
expect "quoted hooks in a CRLF frontmatter fail" fail "$dir"

[ "$failures" -eq 0 ] || { echo "$failures failure(s)"; exit 1; }
echo "all crew trust-boundary cases pass"
