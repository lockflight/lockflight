#!/usr/bin/env bash
# Lifecycle end to end on a copy of samples/dotnet-single: spec → approve → tasks → approve → red/green cycles → trace, with the
# negative controls (agent approval refused, cheating red rejected, test edits blocked during red, a person's amend unlocking one
# test file until its red is re-checked, an amend after the implementation re-checked on the red-time snapshot, a test file
# edited behind the guard refused at green, stubbed green rejected, a test switched off with Explicit refused, a test renamed
# after green dropped from the trace, a test.stopFilter keeping a Docker test out of the regression passes).
# Plays the agent (CLAUDECODE=1) and the human (CLAUDECODE unset). Usage: ENGINE=<lockflight binary> lifecycle.sh <workdir>
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
E="${ENGINE:?path to the lockflight binary}"
W="$1"; rm -rf "${W:?}"; mkdir -p "$W"
cp -R "$ROOT/samples/dotnet-single/." "$W/"; rm -rf "$W"/*/*/bin "$W"/*/*/obj
# The container smoke test needs Docker, which macOS and Windows runners lack: test.stopFilter leaves it out of red/green's
# regression passes (and of the Stop gate); the pr gate and CI would run it.
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
cd "$W" || exit 1; export GIT_AUTHOR_NAME=dev GIT_AUTHOR_EMAIL=dev@example.com GIT_COMMITTER_NAME=dev GIT_COMMITTER_EMAIL=dev@example.com
git init -q; git config user.email dev@example.com
printf 'version: 1\ngates:\n  test:\n    stopFilter: "Category!=Integration"\n' > lockflight.yaml
printf 'bin/\nobj/\n.lockflight/ledger/\n' > .gitignore; git add -A; git commit -qm base
human() { env -u CLAUDECODE "$E" "$@"; }
# Hook payloads carry native paths; on Windows (Git Bash) use the mixed form D:/a/... so the JSON needs no escaping.
NATIVE_W="$W"; command -v cygpath >/dev/null 2>&1 && NATIVE_W="$(cygpath -m "$W")"
hook() { printf '{"hook_event_name":"PreToolUse","cwd":"%s","tool_name":"Edit","tool_input":{"file_path":"%s"}}' "$NATIVE_W" "$NATIVE_W/$1" | CLAUDE_PROJECT_DIR="$NATIVE_W" "$E" hook pre-tool-use; }
bash_hook() { printf '{"hook_event_name":"PreToolUse","cwd":"%s","tool_name":"Bash","tool_input":{"command":"%s"}}' "$NATIVE_W" "$1" | CLAUDE_PROJECT_DIR="$NATIVE_W" "$E" hook pre-tool-use; }
F=specs/20261011-120000-shipping; mkdir -p $F
cat > $F/spec.md <<'MD'
# Feature Specification: Shipping cost

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Pay for shipping (Priority: P1)

1. **Given** a 2.1 kg parcel and a 10.00 order, **When** shipping is priced, **Then** it costs 10.00

### User Story 2 - Free shipping (Priority: P2)

1. **Given** an order of exactly 50.00, **When** shipping is priced, **Then** it is free

## Requirements *(mandatory)*

- **FR-001**: System MUST charge 5.00 for the first kilogram
- **FR-002**: System MUST charge 2.50 for every further started kilogram
- **FR-003**: Orders of 50.00 or more MUST ship free
MD
expect 0 "specify phase passes" -- "$E" phase check specify
expect 1 "tasks phase fails before approval and tasks.md" -- "$E" phase check tasks
expect 2 "the agent cannot approve" "records a human decision" -- "$E" approve spec
expect 0 "a person approves the spec" -- human approve spec
cat > $F/tasks.md <<'MD'
# Tasks: Shipping cost

- [ ] T001 Confirm the sample builds

## Phase 3: User Story 1 - Pay for shipping (Priority: P1)

- [ ] T010 [US1] (FR-001, FR-002) Declare ShippingCost stub in src/Shop.Api/PriceCalculator.cs

### Tests for User Story 1 (MANDATORY — write first, ensure they FAIL) ⚠️

- [ ] T011 [US1] (FR-001, FR-002) Shipping tests in tests/Shop.Tests/ShippingTests.cs

### Implementation for User Story 1

- [ ] T012 [US1] (FR-001, FR-002) Implement ShippingCost

## Phase 4: User Story 2 - Free shipping (Priority: P2)

### Tests for User Story 2 (MANDATORY — write first, ensure they FAIL) ⚠️

- [ ] T020 [US2] (FR-003) Free-shipping tests in tests/Shop.Tests/FreeShippingTests.cs

### Implementation for User Story 2

- [ ] T021 [US2] (FR-003) Free shipping at 50.00 or more
MD
expect 0 "tasks phase passes" -- "$E" phase check tasks
expect 1 "red/green needs the tasks approval" -- "$E" redgreen start
expect 0 "a person approves the tasks" -- human approve tasks
expect 0 "baseline taken; its regression pass leaves out the Docker test by test.stopFilter" "test.stopFilter 'Category!=Integration' left out 1 test" -- "$E" redgreen start
expect 0 "status shows the baseline's regression filter" "baseline regression pass: test.stopFilter 'Category!=Integration' left out 1 test" -- "$E" redgreen status
expect 1 "baseline is write-once" -- "$E" redgreen start
calc=src/Shop.Api/PriceCalculator.cs
# Declare the stub: replace the class's closing brace.
perl -0pi -e 's/\}\s*\z/\n    \/\/\/ <summary>Shipping cost (FR-001, FR-002, FR-003).<\/summary>\n    public decimal ShippingCost(decimal weightKg, decimal subtotal) => throw new NotImplementedException();\n}\n/' "$calc"
cat > tests/Shop.Tests/ShippingTests.cs <<'CS'
using Shop.Api;

namespace Shop.Tests;

public sealed class ShippingTests
{
    [Fact]
    [Trait("Requirement", "FR-001")]
    public void FirstKilogramCostsFive() => Assert.Equal(5m, new PriceCalculator().ShippingCost(1m, 10m));

    [Fact]
    [Trait("Requirement", "FR-002")]
    public void EveryStartedKilogramCostsTwoFifty() => Assert.Equal(10m, new PriceCalculator().ShippingCost(2.1m, 10m));

    [Fact]
    [Trait("Requirement", "FR-002")]
    public void Cheat() => throw new NotImplementedException();
}
CS
expect 1 "a test throwing in its own code is rejected" "RED_IN_TEST" -- "$E" redgreen red
# Reds that never reach production code are rejected, each with its reason: an assertion that compares constants, an
# exception from the runtime, Assert.Fail, and a class fixture that throws.
cat > tests/Shop.Tests/ShippingTests.cs <<'CS'
using Shop.Api;

namespace Shop.Tests;

public sealed class BrokenFixture
{
    public BrokenFixture() => throw new InvalidOperationException("setup");
}

public sealed class ShippingTests(BrokenFixture fixture) : IClassFixture<BrokenFixture>
{
    [Fact]
    [Trait("Requirement", "FR-001")]
    public void FirstKilogramCostsFive() => Assert.NotNull(fixture);
}

public sealed class FakeRedTests
{
    [Fact]
    [Trait("Requirement", "FR-001")]
    public void ComparesConstants() => Assert.Equal(1, 2);

    [Fact]
    [Trait("Requirement", "FR-001")]
    public void ThrowsFromTheRuntime() => Assert.Equal(1, Array.Empty<int>().First());

    [Fact]
    [Trait("Requirement", "FR-001")]
    public void FailsUnconditionally()
    {
        _ = new PriceCalculator();
        Assert.Fail("todo");
    }
}
CS
expect 1 "an assertion without a production call is rejected" "RED_NO_PRODUCTION" -- "$E" redgreen red
expect 1 "a rejected red says why" "fails, but no production call found" -- "$E" redgreen red
expect 1 "an exception from the runtime is rejected" "RED_OTHER" -- "$E" redgreen red
expect 1 "Assert.Fail and a throwing fixture are rejected" "RED_IN_TEST" -- "$E" redgreen red
write_shipping_tests() {
  cat > tests/Shop.Tests/ShippingTests.cs <<'CS'
using Shop.Api;

namespace Shop.Tests;

public sealed class ShippingTests
{
    [Fact]
    [Trait("Requirement", "FR-001")]
    public void FirstKilogramCostsFive() => Assert.Equal(5m, new PriceCalculator().ShippingCost(1m, 10m));

    [Fact]
    [Trait("Requirement", "FR-002")]
    public void EveryStartedKilogramCostsTwoFifty() => Assert.Equal(10m, new PriceCalculator().ShippingCost(2.1m, 10m));
}
CS
}
write_shipping_tests
expect 0 "an honest stub red is accepted, its deciding frame shown" "PriceCalculator.cs:" -- "$E" redgreen red --verbose
expect 2 "test edits are blocked during red" "tests are locked during red cycle 1" -- hook tests/Shop.Tests/ShippingTests.cs
expect 0 "production edits are allowed during red" -- hook src/Shop.Api/PriceCalculator.cs
expect 2 "git checkout of a locked test file is blocked" "red cycle 1" -- bash_hook "git checkout HEAD -- tests/Shop.Tests/ShippingTests.cs"
expect 2 "sed -i on a locked test file is blocked" "red cycle 1" -- bash_hook "sed -i 's/5m/6m/' tests/Shop.Tests/ShippingTests.cs"
expect 2 "a wildcard rm over the locked tests is blocked" "red cycle 1" -- bash_hook "rm tests/Shop.Tests/*.cs"
expect 2 "dotnet format over the locked tests is blocked" "red cycle 1" -- bash_hook "dotnet format"
# An edit the guard did not see (another editor, a script) is caught at green by the test files' red-time hashes.
printf '\n// edited behind the guard\n' >> tests/Shop.Tests/PriceCalculatorTests.cs
expect 1 "a test file edited after red without an amend is refused at green" "tests/Shop.Tests/PriceCalculatorTests.cs (changed)" -- "$E" redgreen green
git checkout -q -- tests/Shop.Tests/PriceCalculatorTests.cs
# A test whose expectation is wrong changes only with a person's amend, which unlocks the file that declares it until a red
# re-checks the test.
expect 2 "the agent cannot amend a test" "records a human decision" -- "$E" redgreen amend EveryStartedKilogramCostsTwoFifty --reason "wrong weight"
expect 0 "a person amends one test of the cycle" "tests/Shop.Tests/ShippingTests.cs (the file declaring" -- \
  human redgreen amend ShippingTests.EveryStartedKilogramCostsTwoFifty --reason "the example should use a whole second kilogram"
expect 0 "the amended test file is writable" -- hook tests/Shop.Tests/ShippingTests.cs
expect 2 "other test files stay locked during the amend" "red cycle 1" -- hook tests/Shop.Tests/PriceCalculatorTests.cs
expect 1 "green is refused while the amend waits for its red" "amended and not re-checked" -- "$E" redgreen green
perl -pi -e 's/Assert\.Equal\(10m, new PriceCalculator\(\)\.ShippingCost\(2\.1m, 10m\)\)/Assert.Equal(7.5m, new PriceCalculator().ShippingCost(2m, 10m))/' tests/Shop.Tests/ShippingTests.cs
expect 0 "a red re-checks only the amended test" "RED_STUB     Shop.Tests.ShippingTests.EveryStartedKilogramCostsTwoFifty (amended)" -- "$E" redgreen red
expect 2 "the amended file is locked again after its red" "red cycle 1" -- hook tests/Shop.Tests/ShippingTests.cs
expect 0 "status shows the amend" "amended: ShippingTests.EveryStartedKilogramCostsTwoFifty in cycle 1" -- "$E" redgreen status
expect 1 "green is refused while stubbed, before the full suite" "the rest of the suite was not run" -- "$E" redgreen green
perl -pi -e 's/=> throw new NotImplementedException\(\);/=> weightKg <= 1m ? 5m : 5m + Math.Ceiling(weightKg - 1m) * 2.5m;/' "$calc"
expect 0 "green closes cycle 1; its regression pass leaves out the Docker test" "Regression pass: test.stopFilter 'Category!=Integration' left out 1 test" -- "$E" redgreen green
expect 0 "status records the green's regression filter" "regression pass: test.stopFilter 'Category!=Integration' left out 1 test (the pr gate and CI run them)" -- "$E" redgreen status
# Approvals bind the artifacts: a red against tasks nobody approved is refused until the user approves again.
approved_tasks="$(cat $F/tasks.md)"
printf '\n- [ ] T030 [US2] (FR-003) Added after the approval\n' >> $F/tasks.md
expect 1 "red is refused while the tasks approval is stale" "tasks approval is stale" -- "$E" redgreen red
printf '%s\n' "$approved_tasks" > $F/tasks.md
cat > tests/Shop.Tests/FreeShippingTests.cs <<'CS'
using Shop.Api;

namespace Shop.Tests;

public sealed class FreeShippingTests
{
    [Theory]
    [Trait("Requirement", "FR-003")]
    [InlineData(50)]
    [InlineData(80)]
    public void OrdersOfFiftyOrMoreShipFree(decimal subtotal) => Assert.Equal(0m, new PriceCalculator().ShippingCost(3m, subtotal));

    [Fact]
    [Trait("Requirement", "FR-003")]
    public void FreeShippingStartsAtFifty() => Assert.Equal(0m, new PriceCalculator().ShippingCost(3m, 49m));
}
CS
# Switching off a test that already went green (xUnit v3 Explicit) is caught before a red and at a green.
make_first_explicit() { perl -0pi -e 's/\[Fact\](\s*\[Trait\("Requirement", "FR-001"\)\])/[Fact(Explicit = true)]$1/' tests/Shop.Tests/ShippingTests.cs; }
make_first_explicit
expect 1 "a red is refused after an earlier test was marked Explicit" "known tests newly marked Explicit" -- "$E" redgreen red
write_shipping_tests
expect 0 "cycle 2 assertion red accepted" "RED_ASSERT" -- "$E" redgreen red
perl -pi -e 's/=> weightKg <= 1m \? 5m/=> subtotal >= 50m ? 0m : weightKg <= 1m ? 5m/' "$calc"
# The implementation exists and shows a test of this cycle is wrong (free shipping starts at 50.00, not 49.00). The amended test
# passes on the current code, so its red is re-checked on the red-time snapshot, where it still fails.
expect 1 "green shows the wrong test" "cycle tests not passing: Shop.Tests.FreeShippingTests.FreeShippingStartsAtFifty" -- "$E" redgreen green
expect 0 "a person amends the wrong test after the implementation" -- \
  human redgreen amend FreeShippingStartsAtFifty --reason "free shipping starts at 50.00, the spec's boundary"
perl -pi -e 's/ShippingCost\(3m, 49m\)/ShippingCost(3m, 50m)/' tests/Shop.Tests/FreeShippingTests.cs
expect 0 "the amended test is re-checked on the red-time snapshot" "RED_ASSERT   Shop.Tests.FreeShippingTests.FreeShippingStartsAtFifty (amended)" -- "$E" redgreen red
expect 0 "status shows the amend's diff" "+1 -1 lines in tests/Shop.Tests/FreeShippingTests.cs" -- "$E" redgreen status
expect 0 "the pull request summary shows the amend's diff" "+    public void FreeShippingStartsAtFifty() => Assert.Equal(0m, new PriceCalculator().ShippingCost(3m, 50m));" -- "$E" review status
make_first_explicit
expect 1 "a green is refused after an earlier test was marked Explicit behind the guard" "tests/Shop.Tests/ShippingTests.cs (changed) changed after red cycle 2" -- "$E" redgreen green
write_shipping_tests
expect 0 "green closes cycle 2" -- "$E" redgreen green

expect 0 "every requirement traced" "3 of 3 requirements covered" -- "$E" trace
expect 0 "trace names the amended tests" "2 tests were changed after their red" -- "$E" trace
# Trace re-checks the evidence against the current tests: a test renamed after its green no longer counts.
perl -pi -e 's/FirstKilogramCostsFive\(\)/FirstKilogramCostsFiveRenamed()/' tests/Shop.Tests/ShippingTests.cs
expect 1 "trace fails when a requirement loses its last red/green test" "FR-001 lost every test" -- "$E" trace
write_shipping_tests
expect 0 "trace passes again once the test is back" "3 of 3 requirements covered" -- "$E" trace
approved_spec="$(cat $F/spec.md)"
printf '\nA note added after the approval.\n' >> $F/spec.md
expect 0 "trace warns about a stale spec approval" "spec approval is stale" -- "$E" trace
printf '%s\n' "$approved_spec" > $F/spec.md

# An untouched Spec Kit template is not a spec.
mkdir -p specs/999-template
cat > specs/999-template/spec.md <<'MD'
# Feature Specification: [FEATURE NAME]

### User Story 1 - [Brief Title] (Priority: P1)

1. **Given** [initial state], **When** [action], **Then** [expected outcome]

- **FR-001**: System MUST [specific capability, e.g., "allow users to create accounts"]
MD
expect 1 "an untouched template fails the specify phase" "template placeholders left" -- "$E" phase check specify --feature 999-template
rm -rf specs/999-template

# The format gate judges the change, not the file: LF files under an .editorconfig that asks for CRLF (as on Parlot).
git add -A; git commit -qm feature
printf 'root = true\n\n[*.cs]\nend_of_line = crlf\nindent_style = space\nindent_size = 4\n' > .editorconfig
git add .editorconfig; git commit -qm editorconfig
perl -0pi -e 's/\}\s*\z/\n    public decimal Half(decimal value) => value \/ 2;\n}\n/' "$calc"
expect 0 "format passes a clean change in a file with other line endings" "end-of-line-only differences not attributed" -- "$E" gate run --profile edit
perl -0pi -e 's/\}\s*\z/\n  public decimal Twice(decimal value) => value * 2;\n}\n/' "$calc"
expect 1 "format fails a badly indented changed line" "src/Shop.Api/PriceCalculator.cs:" -- "$E" gate run --profile edit
git checkout -q -- "$calc"
# A person's reset discards the evidence and the refs that keep its cycles' red-time snapshots.
expect 0 "a person's reset deletes the cycles' snapshot refs" "Deleted the red-time snapshot refs refs/lockflight/red/" -- human redgreen reset
expect 0 "no snapshot ref is left" -- sh -c 'test -z "$(git for-each-ref refs/lockflight)"'
echo
if [ "$failures" -ne 0 ]; then echo "lifecycle e2e: $failures failed"; exit 1; fi
echo "lifecycle e2e: all checks passed"
