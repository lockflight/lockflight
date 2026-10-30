#!/usr/bin/env bash
# A project whose local ledger holds this month's events: the agent tried to edit lockflight.yaml three times, the pr gate
# failed on build twice, one red was rejected (RED_IN_TEST), and the passing gate runs and an approval that are not incidents.
# The stand-in prints facts naming the ledger file, as the real engine would.
set -euo pipefail
here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
standin="$here/../_fixtures/facts-standin.sh"
[ -f "$standin" ] || { echo "scaffold: engine stand-in not found at $standin" >&2; exit 1; }
mkdir -p .lockflight/bin .lockflight/ledger src/Shop
cp "$standin" .lockflight/bin/lockflight && chmod +x .lockflight/bin/lockflight
printf 'version: 1\n' > lockflight.yaml
printf '.lockflight/\n' > .gitignore
printf 'namespace Shop;\n\npublic sealed record Order(string Id);\n' > src/Shop/Order.cs
now=$(date -u +%Y-%m-%dT%H:%M:%SZ)
month=$(date -u +%Y-%m)
ledger=".lockflight/ledger/events-$month.jsonl"
line() { printf '{"schema":"lockflight.ledger/v1","id":"%s","ts":"%s","type":"%s","signature":"%s","summary":"%s"%s}\n' "$1" "$now" "$2" "$3" "$4" "${5:-}" >> "$ledger"; }
line a1 hook.blocked "pre-tool-use:protected-path:lockflight.yaml" "Edit of lockflight.yaml blocked: protected by lockflight.yaml"
line a2 gate.completed "gate:pr:FAIL:build" "pr FAIL: build (2 new warnings)" ',"trigger":"command","durationMs":41000,"cache":"none"'
line a3 hook.blocked "pre-tool-use:protected-path:lockflight.yaml" "Bash write to lockflight.yaml blocked: protected by lockflight.yaml"
line a4 evidence.recorded "redgreen:red:rejected" "red rejected: RED_IN_TEST"
line a5 gate.completed "gate:pr:FAIL:build" "pr FAIL: build (1 new warning)" ',"trigger":"command","durationMs":39000,"cache":"none"'
line a6 gate.completed "gate:pr:PASS:" "pr PASS" ',"trigger":"command","durationMs":52000,"cache":"none"'
line a7 approval.recorded "approve:spec" "001-orders spec approved" ',"feature":"001-orders","phase":"spec","artifacts":"spec.md=1a2b3c4d5e6f","channel":"session"'
line a8 hook.blocked "pre-tool-use:protected-path:lockflight.yaml" "Write of lockflight.yaml blocked: protected by lockflight.yaml"
cat > .lockflight/facts.json <<JSON
{"schema":"lockflight.facts/v1","engineVersion":"0.3.0","manifest":{"file":"lockflight.yaml","stopGate":false,"stopTarget":null,"units":[],"shell":null,"review":{"model":"sonnet","budgetUsd":3.00,"target":null}},"protectedPaths":["lockflight.yaml",".lockflight/**","**/.editorconfig"],"gates":{"timeoutSeconds":600,"build":null,"testFramework":null,"coverage":{"collector":"none","patchThreshold":80,"minChangedLines":10},"profiles":[{"name":"pr","steps":[{"name":"policy.protected","scope":"root","mode":"report"},{"name":"format","scope":"root","mode":"fail"},{"name":"build","scope":"unit","mode":"fail"},{"name":"test","scope":"unit","mode":"fail"}]}]},"instructionFiles":[],"analyzers":[],"bannedSymbols":[],"codeMetrics":[],"architectureTests":{"present":false,"files":[]},"humanOnlyCommands":["approve spec","approve tasks","update"],"ledger":{"directory":".lockflight/ledger","schema":"lockflight.ledger/v1","files":["$ledger"]}}
JSON
git init -q && git add -A && git -c user.name=eval -c user.email=eval@example.com commit -qm "shop" >/dev/null
