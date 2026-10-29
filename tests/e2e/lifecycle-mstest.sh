#!/usr/bin/env bash
# Red/green and trace on MSTest, on a copy of samples/dotnet-mstest: on Microsoft.Testing.Platform (the sample as it is, MSTest's
# runner with its TRX report), then through VSTest (the same sample without MSTest's runner). Tests are listed from assembly
# metadata; a stub's NotImplementedException is RED_STUB in every [DataRow] row; an MSTest assertion is RED_ASSERT, also in a
# [DynamicData] test; Assert.Fail is rejected as RED_IN_TEST and Assert.Inconclusive as SKIPPED; a test passing before the code
# exists is rejected; green needs the cycle's tests to pass; trace maps the [TestProperty("Requirement", …)] values.
# Usage: ENGINE=<lockflight binary> lifecycle-mstest.sh <workdir>
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
  cp -R "$ROOT/samples/dotnet-mstest/." "$W/"; rm -rf "$W"/*/*/bin "$W"/*/*/obj
  cd "$W" || exit 1
  if [ "$2" = vstest ]; then
    # MSTest through VSTest: no MSTest runner, and dotnet test left on VSTest.
    awk '!/EnableMSTestRunner/' tests/Lending.Tests/Lending.Tests.csproj > csproj.new && mv csproj.new tests/Lending.Tests/Lending.Tests.csproj
    printf '{\n  "sdk": { "version": "10.0.102", "rollForward": "latestFeature" }\n}\n' > global.json
  fi
  # A test that needs a database: test.stopFilter keeps it out of red/green's regression passes (the pr gate and CI run it).
  cat > tests/Lending.Tests/DatabaseTests.cs <<'CS'
namespace Lending.Tests;

[TestClass]
public sealed class DatabaseTests
{
    [TestMethod]
    [TestCategory("Integration")]
    public void NeedsADatabase() => Assert.Fail("no database here");
}
CS
  git init -q; git config user.email dev@example.com
  printf 'version: 1\ngates:\n  test:\n    stopFilter: "TestCategory!=Integration"\n' > lockflight.yaml
  printf 'bin/\nobj/\n.lockflight/ledger/\n' > .gitignore; git add -A; git commit -qm base
  F=specs/20261014-120000-loans; mkdir -p $F
  cat > $F/spec.md <<'MD'
# Feature Specification: Loans

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Lend a copy (Priority: P1)

1. **Given** 3 copies of a title, **When** one is lent, **Then** 2 are on the shelf

## Requirements *(mandatory)*

- **FR-010**: Lending MUST take a copy off the shelf
- **FR-011**: A title never lent MUST have every copy on the shelf
MD
  cat > $F/tasks.md <<'MD'
# Tasks: Loans

## Phase 3: User Story 1 - Lend a copy (Priority: P1)

- [ ] T010 [US1] (FR-010, FR-011) Declare Catalog.Lend and Catalog.OnShelf stubs in src/Lending/Catalog.cs

### Tests for User Story 1 (MANDATORY — write first, ensure they FAIL) ⚠️

- [ ] T011 [US1] (FR-010, FR-011) Loan tests in tests/Lending.Tests/LoanTests.cs

### Implementation for User Story 1

- [ ] T012 [US1] (FR-010, FR-011) Implement Lend and OnShelf
MD
  expect 0 "[$2] a person approves the spec" -- human approve spec
  expect 0 "[$2] a person approves the tasks" -- human approve tasks
}

# cycle_one <platform label>: the stub cycle, then green and trace.
cycle_one() {
  expect 0 "[$1] redgreen start lists the MSTest tests from metadata" "3 tests" -- "$E" redgreen start
  expect 0 "[$1] the baseline's regression pass left the database test out" "test.stopFilter 'TestCategory!=Integration' left out 1 test" -- "$E" redgreen status
  insert_before src/Lending/Catalog.cs "    public int Copies(string title) => copies.GetValueOrDefault(title);" \
    "    public void Lend(string title) => throw new NotImplementedException();

    public int OnShelf(string title) => throw new NotImplementedException();
"
  cat > tests/Lending.Tests/LoanTests.cs <<'CS'
namespace Lending.Tests;

[TestClass]
public sealed class LoanTests
{
    [TestMethod]
    [DataRow(3, 2)]
    [DataRow(1, 0)]
    [TestProperty("Requirement", "FR-010")]
    public void LendingTakesACopyOffTheShelf(int copies, int onShelf)
    {
        Catalog catalog = new();
        catalog.AddCopies("Dune", copies);
        catalog.Lend("Dune");

        Assert.AreEqual(onShelf, catalog.OnShelf("Dune"));
    }

    [TestMethod]
    [TestProperty("Requirement", "FR-011")]
    public void ATitleNeverLentIsAllOnTheShelf()
    {
        Catalog catalog = new();
        catalog.AddCopies("Emma", 2);

        Assert.AreEqual(2, catalog.OnShelf("Emma"));
    }
}
CS
  expect 0 "[$1] a NotImplementedException stub is an accepted red in every row (RED_STUB)" "RED_STUB" -- "$E" redgreen red
  awk '{
    if ($0 ~ /public void Lend\(string title\) => throw/) print "    public void Lend(string title) => lent[title] = lent.GetValueOrDefault(title) + 1;";
    else if ($0 ~ /public int OnShelf\(string title\) => throw/) print "    public int OnShelf(string title) => Copies(title) - lent.GetValueOrDefault(title);";
    else if ($0 ~ /private readonly Dictionary<string, int> copies/) { print; print "\n    private readonly Dictionary<string, int> lent = new(StringComparer.Ordinal);" }
    else print }' src/Lending/Catalog.cs > Catalog.new && mv Catalog.new src/Lending/Catalog.cs
  expect 0 "[$1] green: the cycle's tests and the suite pass, the database test left out" "Regression pass: test.stopFilter 'TestCategory!=Integration' left out 1 test" -- "$E" redgreen green
  expect 0 "[$1] trace maps both requirements to their red-then-green tests" "FR-011" -- "$E" trace
}

# --- Microsoft.Testing.Platform ----------------------------------------------------------------------------------------
setup "$BASE/mtp" mtp
cycle_one mtp

# A test that passes before any code changes is rejected.
cat > tests/Lending.Tests/AlreadyTrueTests.cs <<'CS'
namespace Lending.Tests;

[TestClass]
public sealed class AlreadyTrueTests
{
    [TestMethod]
    [TestProperty("Requirement", "FR-010")]
    public void AlreadyPasses() => Assert.AreEqual(0, new Catalog().Copies("Dune"));
}
CS
expect 1 "[mtp] a test that passes before the code exists is rejected" "GREEN_BEFORE" -- "$E" redgreen red
rm tests/Lending.Tests/AlreadyTrueTests.cs

# Assert.Fail fails whatever the production code does; Assert.Inconclusive does not finish the test.
cat > tests/Lending.Tests/CheatTests.cs <<'CS'
namespace Lending.Tests;

[TestClass]
public sealed class CheatTests
{
    [TestMethod]
    [TestProperty("Requirement", "FR-010")]
    public void FailsOnItsOwn()
    {
        _ = new Catalog().Copies("Dune");
        Assert.Fail("not written yet");
    }

    [TestMethod]
    [TestProperty("Requirement", "FR-010")]
    public void GivesUp()
    {
        _ = new Catalog().Copies("Dune");
        Assert.Inconclusive("later");
    }
}
CS
expect 1 "[mtp] Assert.Fail is rejected as a red in test code" "RED_IN_TEST" -- "$E" redgreen red
expect 1 "[mtp] Assert.Inconclusive is rejected as a test that did not run" "SKIPPED" -- "$E" redgreen red
rm tests/Lending.Tests/CheatTests.cs

# An MSTest assertion on a value production code returns is an accepted red, also with [DynamicData] rows: MSTest prints no
# exception type for it.
insert_before src/Lending/Catalog.cs "    public int Copies(string title) => copies.GetValueOrDefault(title);" \
  "    public bool IsListed(string title) => false;
"
cat > tests/Lending.Tests/ListingTests.cs <<'CS'
namespace Lending.Tests;

[TestClass]
public sealed class ListingTests
{
    public static IEnumerable<object[]> Titles => [["Dune"], ["Emma"]];

    [TestMethod]
    [DynamicData(nameof(Titles))]
    [TestProperty("Requirement", "FR-011")]
    public void AnAddedTitleIsListed(string title)
    {
        Catalog catalog = new();
        catalog.AddCopies(title, 1);

        Assert.IsTrue(catalog.IsListed(title));
    }
}
CS
expect 0 "[mtp] an MSTest assertion failure on a production value is an accepted red (RED_ASSERT)" "RED_ASSERT" -- "$E" redgreen red
awk '{ if ($0 ~ /public bool IsListed\(string title\) => false;/) print "    public bool IsListed(string title) => copies.ContainsKey(title);"; else print }' \
  src/Lending/Catalog.cs > Catalog.new && mv Catalog.new src/Lending/Catalog.cs
expect 0 "[mtp] green with [DynamicData] rows" -- "$E" redgreen green

# --- VSTest -------------------------------------------------------------------------------------------------------------
setup "$BASE/vstest" vstest
cycle_one vstest

echo "lifecycle (MSTest / MTP and VSTest): $([ "$failures" = 0 ] && echo "all checks passed" || echo "$failures failed")"
[ "$failures" = 0 ]
