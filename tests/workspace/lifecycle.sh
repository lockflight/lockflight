#!/usr/bin/env bash
# The lifecycle across a workspace: the spec and tasks live in the shell, the code and tests in a child repository.
# Tasks name the unit with [unit:…], so red/green builds and runs only that unit; the test lock covers the unit's test project;
# trace maps the requirements; adopted features need no approval in `approve verify --base`.
# Usage: ENGINE=<lockflight binary> tests/workspace/lifecycle.sh <empty work directory>
set -uo pipefail
REPO="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="${1:?work directory}"
E="${ENGINE:?set ENGINE to a lockflight binary}"
"$REPO/tests/workspace/make-shell.sh" "$WORK/ws" > /dev/null || exit 1
WS="$WORK/ws"; cd "$WS" || exit 1
export GIT_AUTHOR_NAME=dev GIT_AUTHOR_EMAIL=dev@example.com GIT_COMMITTER_NAME=dev GIT_COMMITTER_EMAIL=dev@example.com CLAUDECODE=1
git config user.email dev@example.com
printf 'version: 1\nunits:\n  - path: services/orders\n    build: Orders.sln\n  - path: services/billing\n  - path: web/admin\n    kind: none\n' > lockflight.yaml
printf '.lockflight/ledger/\n.lockflight/local/\n' >> .gitignore
git add -A && git commit -qm "lockflight"

failures=0
expect() { # expect <exit code> <description> [<text the output must contain>] -- command…
  local want="$1" what="$2" needle=""; shift 2
  if [ "$1" != "--" ]; then needle="$1"; shift; fi
  shift
  local out; out="$("$@" 2>&1)"; local got=$?
  if [ "$got" = "$want" ] && { [ -z "$needle" ] || printf '%s' "$out" | grep -qF -- "$needle"; }; then
    echo "ok   $what"
  else
    echo "FAIL $what (exit $got, want $want${needle:+, output must contain '$needle'})"; echo "$out" | sed 's/^/     /'; failures=$((failures + 1))
  fi
}
human() { env -u CLAUDECODE "$E" "$@"; }
# Hook payloads carry native paths; on Windows (Git Bash) use the mixed form D:/a/... so the JSON needs no escaping.
NATIVE="$WS"; command -v cygpath > /dev/null 2>&1 && NATIVE="$(cygpath -m "$WS")"
hook() { printf '{"hook_event_name":"PreToolUse","cwd":"%s","tool_name":"Edit","tool_input":{"file_path":"%s/%s"}}' "$NATIVE" "$NATIVE" "$1" \
  | CLAUDE_PROJECT_DIR="$NATIVE" "$E" hook pre-tool-use; }

ID=20261012-130000-line-count; F=specs/$ID; mkdir -p $F
cat > $F/spec.md <<'MD'
# Feature Specification: Line count

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Count the lines (Priority: P1)

1. **Given** an order with two lines, **When** its lines are counted, **Then** the count is 2

## Requirements *(mandatory)*

- **FR-010**: An order MUST report how many lines it has
MD
cat > $F/tasks.md <<'MD'
# Tasks: Line count

## Phase 3: User Story 1 - Count the lines (Priority: P1)

- [ ] T010 [US1] (FR-010) [unit:services/orders] Declare Order.LineCount stub in src/Orders/Order.cs

### Tests for User Story 1 (MANDATORY — write first, ensure they FAIL) ⚠️

- [ ] T011 [US1] (FR-010) [unit:services/orders] Line count tests in tests/Orders.Tests/LineCountTests.cs

### Implementation for User Story 1

- [ ] T012 [US1] (FR-010) [unit:services/orders] Implement LineCount
MD
expect 0 "a person approves the spec" -- human approve spec --feature "$ID"
expect 0 "the tasks phase accepts the unit tags" "every [unit:…] tag names a unit" -- "$E" phase check tasks --feature "$ID"
expect 0 "a person approves the tasks" -- human approve tasks --feature "$ID"
expect 0 "redgreen start baselines only the named unit's tests" "services/orders/tests/Orders.Tests" -- "$E" redgreen start --feature "$ID"

O=services/orders
sed -i.bak 's/    public void Add(OrderLine line) => lines.Add(line);/    public int LineCount => throw new NotImplementedException();\n\n    public void Add(OrderLine line) => lines.Add(line);/' $O/src/Orders/Order.cs && rm $O/src/Orders/Order.cs.bak
cat > $O/tests/Orders.Tests/LineCountTests.cs <<'CS'
using Orders;

namespace Orders.Tests;

public sealed class LineCountTests
{
    [Fact]
    [Trait("Requirement", "FR-010")]
    public void CountsTheLines()
    {
        Order order = new();
        order.Add(new OrderLine("A", 1, 1m));

        Assert.Equal(1, order.LineCount);
    }
}
CS
expect 0 "the child's stub is an accepted red" "RED_STUB" -- "$E" redgreen red --feature "$ID"
expect 2 "the unit's test project is locked during the red" -- hook "$O/tests/Orders.Tests/OrderTests.cs"
sed -i.bak 's/public int LineCount => throw new NotImplementedException();/public int LineCount => lines.Count;/' $O/src/Orders/Order.cs && rm $O/src/Orders/Order.cs.bak
expect 0 "green in the child repository" -- "$E" redgreen green --feature "$ID"
expect 0 "the unit's tests are unlocked after green" -- hook "$O/tests/Orders.Tests/OrderTests.cs"
expect 0 "trace covers the requirement" "1 of 1 requirements covered" -- "$E" trace --feature "$ID"

echo "workspace lifecycle: $([ "$failures" = 0 ] && echo "all checks passed" || echo "$failures failed")"
[ "$failures" = 0 ]
