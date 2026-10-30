#!/usr/bin/env bash
# A small shop with six rules in CLAUDE.md: two the guard backs (a protected path, a human-only command), one an analyzer the
# build sees (CA1502), three with nothing behind them (an API ban, a layer ban, a style rule). The facts are what the real
# engine printed for this tree (`lockflight facts --json`); the stand-in prints them.
set -euo pipefail
here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
standin="$here/../_fixtures/facts-standin.sh"
[ -f "$standin" ] || { echo "scaffold: engine stand-in not found at $standin" >&2; exit 1; }
mkdir -p src/Shop.Domain secrets .lockflight/bin
cp "$standin" .lockflight/bin/lockflight && chmod +x .lockflight/bin/lockflight
printf 'version: 1\nprotected:\n  - "secrets/**"\n' > lockflight.yaml
cat > CLAUDE.md <<'MD'
# Shop

## Rules

- Never edit `secrets/keys.json`; ask a maintainer.
- Never run `lockflight approve spec` yourself.
- Keep cyclomatic complexity low: CA1502 must stay clean.
- Do not use `DateTime.Now`; inject a `TimeProvider`.
- The Domain project must not depend on Infrastructure.
- Always write commit messages in the imperative mood.
MD
printf 'root = true\n\n[*.cs]\ndotnet_diagnostic.CA1502.severity = warning\n' > .editorconfig
printf 'CA1502(Method): 15\n' > CodeMetricsConfig.txt
printf '{}\n' > secrets/keys.json
printf 'namespace Shop.Domain;\n\npublic sealed record Order(string Id);\n' > src/Shop.Domain/Order.cs
printf '.lockflight/\n' > .gitignore
cat > .lockflight/facts.json <<'JSON'
{"schema":"lockflight.facts/v1","engineVersion":"0.3.0","manifest":{"file":"lockflight.yaml","stopGate":false,"stopTarget":null,"units":[],"shell":null,"review":{"model":"sonnet","budgetUsd":3.00,"target":null}},"protectedPaths":["lockflight.yaml","lockflight.unit.yaml","dotnet-tools.json",".config/dotnet-tools.json",".lockflight/**",".claude/settings.json",".claude/settings.local.json",".claude/hooks/**",".claude/agents/**",".claude/skills/**",".claude/commands/**","**/Directory.Build.props","**/Directory.Build.targets","**/Directory.Build.rsp","**/Directory.Packages.props","global.json","**/.editorconfig","**/.globalconfig","**/*.globalconfig","**/*.ruleset","**/stylecop.json","**/BannedSymbols.txt","**/CodeMetricsConfig.txt","**/.gitattributes","**/NuGet.config","**/nuget.config",".gitleaks.toml",".gitleaksignore",".github/workflows/lockflight-gates.yml",".azure-pipelines/lockflight-gates.yml",".gitlab/ci/lockflight-gates.yml",".gitlab-ci.yml","bitbucket-pipelines.yml","secrets/**"],"gates":{"timeoutSeconds":600,"build":null,"testFramework":null,"coverage":{"collector":"none","patchThreshold":80,"minChangedLines":10},"profiles":[{"name":"edit","steps":[{"name":"format","scope":"root","mode":"fail"}]},{"name":"stop","steps":[{"name":"format","scope":"root","mode":"fail"},{"name":"build","scope":"unit","mode":"fail"},{"name":"test","scope":"unit","mode":"fail"}]},{"name":"pr","steps":[{"name":"policy.protected","scope":"root","mode":"report"},{"name":"format","scope":"root","mode":"fail"},{"name":"quality.suppressions","scope":"root","mode":"report"},{"name":"build","scope":"unit","mode":"fail"},{"name":"test","scope":"unit","mode":"fail"},{"name":"coverage.patch","scope":"unit","mode":"fail"},{"name":"security.deps","scope":"unit","mode":"fail"},{"name":"security.secrets","scope":"root","mode":"fail"}]}]},"instructionFiles":["CLAUDE.md"],"analyzers":[{"id":"CA1502","severity":"warning","reachesBuild":true,"file":".editorconfig"}],"bannedSymbols":[],"codeMetrics":[{"rule":"CA1502","target":"Method","threshold":15,"file":"CodeMetricsConfig.txt"}],"architectureTests":{"present":false,"files":[]},"humanOnlyCommands":["approve spec","approve tasks","baseline build","baseline deps","baseline suppressions","baseline tests","redgreen amend","redgreen reset","review dismiss","update"],"ledger":{"directory":".lockflight/ledger","schema":"lockflight.ledger/v1","files":[]}}
JSON
git init -q && git add -A && git -c user.name=eval -c user.email=eval@example.com commit -qm "shop" >/dev/null
