#!/usr/bin/env bash
# Red/green and trace on xUnit v2 over VSTest, on a copy of the workspace sample's orders service (.NET 8): tests are
# listed from assembly metadata, a stub's NotImplementedException is RED_STUB, an assertion failure RED_ASSERT, a test passing
# before the code exists is rejected, green needs the cycle's tests to pass, and trace maps the Requirement traits.
# Usage: ENGINE=<lockflight binary> lifecycle-xunit2.sh <workdir>
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
E="${ENGINE:?path to the lockflight binary}"
W="$1"; rm -rf "${W:?}"; mkdir -p "$W"
cp -R "$ROOT/samples/workspace-shell/children/services/orders/." "$W/"; rm -rf "$W"/*/*/bin "$W"/*/*/obj
export CLAUDECODE=1
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
cd "$W" || exit 1; export GIT_AUTHOR_NAME=dev GIT_AUTHOR_EMAIL=dev@example.com GIT_COMMITTER_NAME=dev GIT_COMMITTER_EMAIL=dev@example.com
git init -q; git config user.email dev@example.com; printf 'version: 1\n' > lockflight.yaml
printf 'bin/\nobj/\n.lockflight/ledger/\n' > .gitignore; git add -A; git commit -qm base
F=specs/20261012-120000-line-count; mkdir -p $F
cat > $F/spec.md <<'MD'
# Feature Specification: Line count

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Count the lines (Priority: P1)

1. **Given** an order with two lines, **When** its lines are counted, **Then** the count is 2

## Requirements *(mandatory)*

- **FR-010**: An order MUST report how many lines it has
- **FR-011**: An order without lines MUST report zero lines
MD
cat > $F/tasks.md <<'MD'
# Tasks: Line count

## Phase 3: User Story 1 - Count the lines (Priority: P1)

- [ ] T010 [US1] (FR-010, FR-011) Declare Order.LineCount in src/Orders/Order.cs

### Tests for User Story 1 (MANDATORY — write first, ensure they FAIL) ⚠️

- [ ] T011 [US1] (FR-010, FR-011) Line count tests in tests/Orders.Tests/LineCountTests.cs

### Implementation for User Story 1

- [ ] T012 [US1] (FR-010, FR-011) Implement LineCount
MD
expect 0 "a person approves the spec" -- human approve spec
expect 0 "a person approves the tasks" -- human approve tasks
expect 0 "redgreen start lists the xUnit v2 tests from metadata" "2 tests" -- "$E" redgreen start

sed -i.bak 's/    public void Add(OrderLine line) => lines.Add(line);/    public int LineCount => throw new NotImplementedException();\n\n    public void Add(OrderLine line) => lines.Add(line);/' src/Orders/Order.cs && rm src/Orders/Order.cs.bak
cat > tests/Orders.Tests/LineCountTests.cs <<'CS'
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
        order.Add(new OrderLine("B", 1, 1m));

        Assert.Equal(2, order.LineCount);
    }

    [Fact]
    [Trait("Requirement", "FR-011")]
    public void AnEmptyOrderHasNoLines() => Assert.Equal(0, new Order().LineCount);
}
CS
expect 0 "a NotImplementedException stub is an accepted red (RED_STUB)" "RED_STUB" -- "$E" redgreen red
sed -i.bak 's/public int LineCount => throw new NotImplementedException();/public int LineCount => lines.Count;/' src/Orders/Order.cs && rm src/Orders/Order.cs.bak
expect 0 "green: the cycle's tests and the suite pass" -- "$E" redgreen green
expect 0 "trace maps both requirements to their red-then-green tests" "FR-011" -- "$E" trace

# A second cycle whose test passes before any code changes is rejected.
cat > tests/Orders.Tests/AlreadyTrueTests.cs <<'CS'
namespace Orders.Tests;

public sealed class AlreadyTrueTests
{
    [Fact]
    [Trait("Requirement", "FR-010")]
    public void AlreadyPasses() => Assert.True(true);
}
CS
expect 1 "a test that passes before the code exists is rejected" "GREEN_BEFORE" -- "$E" redgreen red

# A test that calls a stub but asserts nothing is accepted with an assertion-free warning.
rm tests/Orders.Tests/AlreadyTrueTests.cs
sed -i.bak 's/    public void Add(OrderLine line) => lines.Add(line);/    public void Archive() => throw new NotImplementedException();\n\n    public void Add(OrderLine line) => lines.Add(line);/' src/Orders/Order.cs && rm src/Orders/Order.cs.bak
cat > tests/Orders.Tests/ArchiveTests.cs <<'CS'
namespace Orders.Tests;

public sealed class ArchiveTests
{
    [Fact]
    [Trait("Requirement", "FR-010")]
    public void ArchivesTheOrder() => new Orders.Order().Archive();
}
CS
expect 0 "a red whose test asserts nothing is accepted with an assertion-free warning" "assert nothing (assertion-free): ArchiveTests.ArchivesTheOrder" -- "$E" redgreen red
sed -i.bak 's/public void Archive() => throw new NotImplementedException();/public void Archive() { }/' src/Orders/Order.cs && rm src/Orders/Order.cs.bak
expect 0 "green after the archive cycle" -- "$E" redgreen green

# An xUnit v2 assertion on a value production code returns is an accepted red: VSTest prints no exception type for it.
sed -i.bak 's/    public void Archive() { }/    public bool IsArchived => false;\n\n    public void Archive() { }/' src/Orders/Order.cs && rm src/Orders/Order.cs.bak
cat > tests/Orders.Tests/ArchivedTests.cs <<'CS'
namespace Orders.Tests;

public sealed class ArchivedTests
{
    [Fact]
    [Trait("Requirement", "FR-010")]
    public void AnArchivedOrderSaysSo()
    {
        Orders.Order order = new();
        order.Archive();

        Assert.True(order.IsArchived);
    }
}
CS
expect 0 "an assertion failure on a production value is an accepted red (RED_ASSERT)" "RED_ASSERT" -- "$E" redgreen red

echo "lifecycle (xUnit v2 / VSTest): $([ "$failures" = 0 ] && echo "all checks passed" || echo "$failures failed")"
[ "$failures" = 0 ]
