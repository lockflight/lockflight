#!/usr/bin/env bash
# Red/green and trace on NUnit, on a copy of samples/dotnet-nunit: through VSTest (the sample as it is), then on
# Microsoft.Testing.Platform (the same sample with NUnit's runner and the TRX report enabled). Tests are listed from assembly
# metadata; a stub's NotImplementedException is RED_STUB in every [TestCase] row; an NUnit assertion is RED_ASSERT, also in a
# parameterized fixture; Assert.Fail is rejected as RED_IN_TEST and Assert.Ignore as SKIPPED; a test passing before the code
# exists is rejected; green needs the cycle's tests to pass; trace maps the [Property("Requirement", …)] values.
# Usage: ENGINE=<lockflight binary> lifecycle-nunit.sh <workdir>
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
E="${ENGINE:?path to the lockflight binary}"
BASE="$1"; rm -rf "${BASE:?}"; mkdir -p "$BASE"
export CLAUDECODE=1 GIT_AUTHOR_NAME=dev GIT_AUTHOR_EMAIL=dev@example.com GIT_COMMITTER_NAME=dev GIT_COMMITTER_EMAIL=dev@example.com
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
# insert_before <file> <line to find> <text to put before it>: inserts production code without sed's platform differences.
insert_before() { TEXT="$3" awk -v find="$2" '$0 == find { print ENVIRON["TEXT"] } { print }' "$1" > "$1.new" && mv "$1.new" "$1"; }

# setup <dir> <platform label>: a fresh copy of the sample in a git repository, with an approved feature.
setup() {
  W="$1"; mkdir -p "$W"
  cp -R "$ROOT/samples/dotnet-nunit/." "$W/"; rm -rf "$W"/*/*/bin "$W"/*/*/obj
  cd "$W" || exit 1
  if [ "$2" = mtp ]; then
    # NUnit's own runner on Microsoft.Testing.Platform, with the TRX report red/green reads, and without VSTest's test host, so
    # nothing can fall back to VSTest.
    awk '/Microsoft.NET.Test.Sdk/ { next } { print } /<IsTestProject>true<\/IsTestProject>/ { print "    <EnableNUnitRunner>true</EnableNUnitRunner>\n    <OutputType>Exe</OutputType>" }
         /<PackageReference Include="NUnit" / { print "    <PackageReference Include=\"Microsoft.Testing.Extensions.TrxReport\" Version=\"2.5.1\" />" }' \
      tests/Inventory.Tests/Inventory.Tests.csproj > csproj.new && mv csproj.new tests/Inventory.Tests/Inventory.Tests.csproj
  fi
  git init -q; git config user.email dev@example.com; printf 'version: 1\n' > lockflight.yaml
  printf 'bin/\nobj/\n.lockflight/ledger/\n' > .gitignore; git add -A; git commit -qm base
  F=specs/20261013-120000-reservations; mkdir -p $F
  cat > $F/spec.md <<'MD'
# Feature Specification: Reservations

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Reserve stock (Priority: P1)

1. **Given** 5 items on hand, **When** 2 are reserved, **Then** 3 are available

## Requirements *(mandatory)*

- **FR-010**: Reserving MUST lower what is available
- **FR-011**: An item without reservations MUST have everything on hand available
MD
  cat > $F/tasks.md <<'MD'
# Tasks: Reservations

## Phase 3: User Story 1 - Reserve stock (Priority: P1)

- [ ] T010 [US1] (FR-010, FR-011) Declare Stock.Reserve and Stock.Available stubs in src/Inventory/Stock.cs

### Tests for User Story 1 (MANDATORY — write first, ensure they FAIL) ⚠️

- [ ] T011 [US1] (FR-010, FR-011) Reservation tests in tests/Inventory.Tests/ReservationTests.cs

### Implementation for User Story 1

- [ ] T012 [US1] (FR-010, FR-011) Implement Reserve and Available
MD
  expect 0 "[$2] a person approves the spec" -- human approve spec
  expect 0 "[$2] a person approves the tasks" -- human approve tasks
}

# cycle_one <platform label>: the stub cycle, then green and trace.
cycle_one() {
  expect 0 "[$1] redgreen start lists the NUnit tests from metadata" "2 tests" -- "$E" redgreen start
  insert_before src/Inventory/Stock.cs "    public int OnHand(string sku) => onHand.GetValueOrDefault(sku);" \
    "    public void Reserve(string sku, int quantity) => throw new NotImplementedException();

    public int Available(string sku) => throw new NotImplementedException();
"
  cat > tests/Inventory.Tests/ReservationTests.cs <<'CS'
namespace Inventory.Tests;

public sealed class ReservationTests
{
    [TestCase(5, 2, 3)]
    [TestCase(5, 5, 0)]
    [Property("Requirement", "FR-010")]
    public void ReservingLowersWhatIsAvailable(int received, int reserved, int available)
    {
        Stock stock = new();
        stock.Receive("A", received);
        stock.Reserve("A", reserved);

        Assert.That(stock.Available("A"), Is.EqualTo(available));
    }

    [Test]
    [Property("Requirement", "FR-011")]
    public void WithoutReservationsEverythingIsAvailable()
    {
        Stock stock = new();
        stock.Receive("A", 4);

        Assert.That(stock.Available("A"), Is.EqualTo(4));
    }
}
CS
  expect 0 "[$1] a NotImplementedException stub is an accepted red in every row (RED_STUB)" "RED_STUB" -- "$E" redgreen red
  awk '{
    if ($0 ~ /public void Reserve\(string sku, int quantity\) => throw/) print "    public void Reserve(string sku, int quantity) => reserved[sku] = reserved.GetValueOrDefault(sku) + quantity;";
    else if ($0 ~ /public int Available\(string sku\) => throw/) print "    public int Available(string sku) => OnHand(sku) - reserved.GetValueOrDefault(sku);";
    else if ($0 ~ /private readonly Dictionary<string, int> onHand/) { print; print "\n    private readonly Dictionary<string, int> reserved = new(StringComparer.Ordinal);" }
    else print }' src/Inventory/Stock.cs > Stock.new && mv Stock.new src/Inventory/Stock.cs
  expect 0 "[$1] green: the cycle's tests and the suite pass" "nothing deleted, skipped or made explicit" -- "$E" redgreen green
  expect 0 "[$1] trace maps both requirements to their red-then-green tests" "FR-011" -- "$E" trace
}

# --- VSTest -------------------------------------------------------------------------------------------------------------
setup "$BASE/vstest" vstest
cycle_one vstest

# A test that passes before any code changes is rejected.
cat > tests/Inventory.Tests/AlreadyTrueTests.cs <<'CS'
namespace Inventory.Tests;

public sealed class AlreadyTrueTests
{
    [Test]
    [Property("Requirement", "FR-010")]
    public void AlreadyPasses() => Assert.That(new Stock().OnHand("A"), Is.EqualTo(0));
}
CS
expect 1 "[vstest] a test that passes before the code exists is rejected" "GREEN_BEFORE" -- "$E" redgreen red
rm tests/Inventory.Tests/AlreadyTrueTests.cs

# Assert.Fail fails whatever the production code does; Assert.Ignore does not run the test.
cat > tests/Inventory.Tests/CheatTests.cs <<'CS'
namespace Inventory.Tests;

public sealed class CheatTests
{
    [Test]
    [Property("Requirement", "FR-010")]
    public void FailsOnItsOwn()
    {
        _ = new Stock().OnHand("A");
        Assert.Fail("not written yet");
    }

    [Test]
    [Property("Requirement", "FR-010")]
    public void IgnoresItself()
    {
        _ = new Stock().OnHand("A");
        Assert.Ignore("later");
    }
}
CS
expect 1 "[vstest] Assert.Fail is rejected as a red in test code" "RED_IN_TEST" -- "$E" redgreen red
expect 1 "[vstest] Assert.Ignore is rejected as a test that did not run" "SKIPPED" -- "$E" redgreen red
rm tests/Inventory.Tests/CheatTests.cs

# An NUnit assertion on a value production code returns is an accepted red, also in a parameterized fixture: NUnit prints no
# exception type, and a FullyQualifiedName filter would not select the fixture's instances.
insert_before src/Inventory/Stock.cs "    public int OnHand(string sku) => onHand.GetValueOrDefault(sku);" \
  "    public bool IsTracked(string sku) => false;
"
cat > tests/Inventory.Tests/TrackingTests.cs <<'CS'
namespace Inventory.Tests;

[TestFixture("A")]
[TestFixture("B")]
public sealed class TrackingTests(string sku)
{
    [Test]
    [Property("Requirement", "FR-011")]
    public void AReceivedItemIsTracked()
    {
        Stock stock = new();
        stock.Receive(sku, 1);

        Assert.That(stock.IsTracked(sku), Is.True);
    }
}
CS
expect 0 "[vstest] an NUnit assertion failure on a production value is an accepted red (RED_ASSERT)" "RED_ASSERT" -- "$E" redgreen red
awk '{ if ($0 ~ /public bool IsTracked\(string sku\) => false;/) print "    public bool IsTracked(string sku) => onHand.ContainsKey(sku);"; else print }' \
  src/Inventory/Stock.cs > Stock.new && mv Stock.new src/Inventory/Stock.cs
expect 0 "[vstest] green with a parameterized fixture's tests" -- "$E" redgreen green

# --- Microsoft.Testing.Platform ----------------------------------------------------------------------------------------
setup "$BASE/mtp" mtp
cycle_one mtp

echo "lifecycle (NUnit / VSTest and MTP): $([ "$failures" = 0 ] && echo "all checks passed" || echo "$failures failed")"
[ "$failures" = 0 ]
