#!/bin/sh
# Workspace gates end to end on a materialized samples/workspace-shell: the guard protects every unit, the stop gate
# builds and tests only changed units with a per-unit cache, a broken unit blocks with the failing test named, and the test gate
# runs both VSTest (xUnit v2, net8) and Microsoft.Testing.Platform (xUnit v3, net10).
# Usage: ENGINE=<lockflight binary> tests/workspace/gates.sh <empty work directory>
set -u
REPO=$(cd "$(dirname "$0")/../.." && pwd)
WORK="${1:?work directory}"
ENGINE="${ENGINE:?set ENGINE to a lockflight binary}"
"$REPO/tests/workspace/make-shell.sh" "$WORK/ws" > /dev/null || exit 1
WS="$WORK/ws"
cd "$WS" || exit 1
printf 'version: 1\nstop:\n  gate: true\nunits:\n  - path: services/orders\n    build: Orders.sln\n  - path: services/billing\n  - path: web/admin\n' > lockflight.yaml
printf 'lockflight.yaml\n.lockflight/\n' >> .git/info/exclude

# Hook payloads and CLAUDE_PROJECT_DIR carry native paths; on Windows (Git Bash) use the mixed form D:/a/... (no JSON escaping).
NATIVE="$WS"; command -v cygpath > /dev/null 2>&1 && NATIVE="$(cygpath -m "$WS")"
pass=0; fail=0
expect() { if eval "$2"; then pass=$((pass+1)); echo "ok   $1"; else fail=$((fail+1)); echo "FAIL $1"; fi; }
stop() { printf '{"hook_event_name":"Stop","cwd":"%s"}' "$NATIVE" | CLAUDE_PROJECT_DIR="$NATIVE" "$ENGINE" hook stop > /dev/null 2> "$WORK/stop.err"; }
edit() { printf '{"hook_event_name":"PreToolUse","cwd":"%s","tool_name":"Edit","tool_input":{"file_path":"%s/%s"}}' "$NATIVE" "$NATIVE" "$1" \
  | CLAUDE_PROJECT_DIR="$NATIVE" "$ENGINE" hook pre-tool-use > /dev/null 2>&1; }

edit services/orders/global.json; expect "a unit's global.json is protected" '[ $? -eq 2 ]'
edit .claude/hooks/contracts-guard.sh; expect "the team's own hook is protected" '[ $? -eq 2 ]'
edit services/orders/src/Orders/Order.cs; expect "ordinary unit code is editable" '[ $? -eq 0 ]'

"$ENGINE" gate run > "$WORK/fresh.txt" 2>&1
expect "a unit never judged is judged once (both .NET units)" 'grep -q "services/orders: test: PASS" "$WORK/fresh.txt" && grep -q "services/billing: test: PASS" "$WORK/fresh.txt"'
"$ENGINE" gate run > "$WORK/clean.txt" 2>&1
expect "a clean workspace whose units passed judges nothing" 'grep -q "Result: N/A" "$WORK/clean.txt"'

sed -i.bak 's/=> failedAttempts < MaxAttempts;/=> failedAttempts >= 0 \&\& failedAttempts < MaxAttempts;/' services/billing/src/Billing/ChargePolicy.cs && rm services/billing/src/Billing/ChargePolicy.cs.bak
"$ENGINE" gate run > "$WORK/billing.txt" 2>&1
expect "only the changed unit is built and tested (MTP)" 'grep -q "services/billing: test: PASS" "$WORK/billing.txt" && ! grep -q "services/orders: build" "$WORK/billing.txt"'

stop; expect "the stop gate passes the changed unit" '[ $? -eq 0 ]'
[ -f .lockflight/local/stop-pass-services-billing ]; expect "its PASS is cached per unit" '[ $? -eq 0 ]'

# Committed changes are judged too: a unit's state is its HEAD plus its uncommitted changes, compared with its last PASS.
git -C services/billing -c user.email=t@t -c user.name=t commit -qam "Guard negative attempts"
stop; expect "a unit's commit is judged at the next stop, and passes" '[ $? -eq 0 ]'
sed -i.bak 's/failedAttempts >= 0 \&\& failedAttempts < MaxAttempts;/failedAttempts > MaxAttempts;/' services/billing/src/Billing/ChargePolicy.cs && rm services/billing/src/Billing/ChargePolicy.cs.bak
git -C services/billing -c user.email=t@t -c user.name=t commit -qam "Break the retry rule"
stop; expect "a committed break in a unit blocks the stop" '[ $? -eq 2 ] && grep -q "services/billing: test=FAIL" "$WORK/stop.err"'
git -C services/billing reset -q --hard HEAD~1
"$ENGINE" gate run > "$WORK/reset.txt" 2>&1
expect "back at its passed commit, the unit is not rerun" 'grep -q "services/billing: N/A" "$WORK/reset.txt" && grep -q "Result: N/A" "$WORK/reset.txt"'

# A committed change to a shared build input outside every unit (the shell's Directory.Build.props) is judged in every unit.
printf '<Project />\n' > Directory.Build.props && git add Directory.Build.props && git -c user.email=t@t -c user.name=t commit -qm "Shared props"
"$ENGINE" gate run > "$WORK/shared.txt" 2>&1
expect "a committed shared build input reruns every .NET unit" 'grep -q "services/orders: test: PASS" "$WORK/shared.txt" && grep -q "services/billing: test: PASS" "$WORK/shared.txt"'

sed -i.bak 's/line.Quantity \* line.UnitPrice/line.Quantity * line.UnitPrice + 1/' services/orders/src/Orders/Order.cs && rm services/orders/src/Orders/Order.cs.bak
stop; expect "a broken VSTest unit blocks the stop" '[ $? -eq 2 ]'
expect "the failing xUnit v2 test is named" 'grep -q "TotalIsTheSumOfQuantityTimesUnitPrice" "$WORK/stop.err"'
expect "the unchanged-since-PASS unit is not rerun" 'grep -q "services/billing=N/A (unchanged since its last PASS)" "$WORK/stop.err"'

# A second solution beside the unit's build: file makes a bare 'dotnet build' ambiguous (MSB1011): commands run for one unit
# must build what the workspace run builds.
cp services/orders/Orders.sln services/orders/Copy.sln
"$ENGINE" gate run --path services/orders > "$WORK/path.txt" 2>&1
expect "gate run --path <unit> builds the unit's build: file" 'grep -q "^build: PASS" "$WORK/path.txt"'
env -u CLAUDECODE "$ENGINE" baseline build --path services/orders > "$WORK/baseline.txt" 2>&1
expect "baseline build --path <unit> builds the unit's build: file" '[ $? -eq 0 ] && [ -f .lockflight/baselines/services/orders/build-diagnostics.json ]'

echo "workspace gates: passed=$pass failed=$fail"
[ "$fail" = 0 ]
