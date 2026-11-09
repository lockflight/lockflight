#!/usr/bin/env bash
# A WPF client and an ASP.NET Core server in one solution (samples/dotnet-desktop, .NET 8 pinned by global.json), with a
# scripted pull request per scenario.
# On Windows, the CI judge: an honest ViewModel feature PASSes (the gates too, under the SDK 8 pin); a feature whose stub is in
# code-behind, with [StaFact] tests that open the window directly and through the Dispatcher, PASSes; a test written after the
# code FAILs red with GREEN_BEFORE; a server-only feature PASSes; a [structural] refactor without a red commit, its spec
# changed under the target branch's CODEOWNERS, PASSes as a structural-only change (red N/A). The Stop gate runs every test project, the UI tests left to CI by test.stopFilter. On both, a
# visual-only XAML change is routed as class fix, and one that wires an event handler fires config.changed.
# Off Windows, the local gates: the Stop gate builds the client with EnableWindowsTargeting, leaves its test projects out with a
# note and runs the server's tests; doctor explains why.
# Usage: ENGINE=<lockflight binary> desktop.sh <workdir>
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
E="${ENGINE:?path to the lockflight binary}"
W="$1"; rm -rf "${W:?}"; mkdir -p "$W/repo" "$W/temp"
cp -R "$ROOT/samples/dotnet-desktop/." "$W/repo/"; rm -rf "$W"/repo/*/*/bin "$W"/repo/*/*/obj
export LOCKFLIGHT_TEMP="$W/temp"
failures=0
PY="$(command -v python3 || command -v python)"
case "$(uname -s)" in MINGW*|MSYS*|CYGWIN*) windows=1 ;; *) windows=0 ;; esac
pass() { echo "ok   $1"; }
fail() { echo "FAIL $1"; failures=$((failures + 1)); }
# check <evidence> <check id> <verdict> [<text its why must contain>]
check() {
  local got
  got=$("$PY" -c 'import json,sys; d=json.load(open(sys.argv[1])); c=next((c for c in d["checks"] if c["id"]==sys.argv[2]), None)
print("none" if c is None else c["verdict"] + "\t" + (c.get("reason") or "") + "\t" + (c.get("why") or ""))' "$1" "$2")
  if [ "${got%%$'\t'*}" = "$3" ] && { [ -z "${4:-}" ] || printf '%s' "$got" | grep -qF -- "$4"; }; then
    pass "$2 is $3${4:+ ($4)}"
  else
    fail "$2 is ${got%%$'\t'*}, want $3${4:+ with '$4'}"
    "$PY" -c 'import json,sys; [print("     ", c["id"], c["verdict"], c.get("reason"), c["why"]) for c in json.load(open(sys.argv[1]))["checks"]]' "$1"
  fi
}
# expect <exit code> <description> [<text the output must contain>] -- command…
expect() {
  local want="$1" what="$2" needle=""; shift 2
  if [ "$1" != "--" ]; then needle="$1"; shift; fi
  shift
  local out; out="$("$@" 2>&1)"; local got=$?
  if [ "$got" = "$want" ] && { [ -z "$needle" ] || printf '%s' "$out" | grep -qF -- "$needle"; }; then
    pass "$what"
  else
    fail "$what (exit $got, want $want${needle:+, output must contain '$needle'})"; echo "$out" | sed 's/^/     /'
  fi
  LAST_OUTPUT="$out"
}

cd "$W/repo" || exit 1
export GIT_AUTHOR_NAME=dev GIT_AUTHOR_EMAIL=dev@example.com GIT_COMMITTER_NAME=dev GIT_COMMITTER_EMAIL=dev@example.com
git init -q -b main; git config commit.gpgsign false; git config core.autocrlf false
# The UI tests start the real client: the Stop gate and red/green's regression passes leave them to CI.
printf 'version: 2\ngates:\n  test:\n    stopFilter: "Category!=UI"\n  quality:\n    suppressions: report\n  docs:\n    references: report\napprovals:\n  check: report\n' > lockflight.yaml
printf 'bin/\nobj/\n.lockflight/ledger/\n.lockflight/local/\n' > .gitignore
git add -A; git commit -qm base; main=$(git rev-parse HEAD)

# A visual-only XAML change in a WPF project is routed by its size, as class fix: a XAML view is neither code nor configuration.
git checkout -q -b 006-xaml "$main"
FROM='<StackPanel Margin="12">' TO='<StackPanel Margin="16">' "$PY" -c 'import os,sys; p=sys.argv[1]; s=open(p).read(); open(p,"w").write(s.replace(os.environ["FROM"], os.environ["TO"]))' src/Desk.Client/MainWindow.xaml
git add -A; git commit -qm "style: a roomier window"
expect 0 "a visual-only XAML change is class fix" "**Class: fix**" -- "$E" route show --base "$main" --head 006-xaml
# A XAML change that wires an event handler is behaviour: config.changed fires.
git checkout -q -b 007-xaml-handler "$main"
FROM='Content="Clear"' TO='Content="Clear" Loaded="ClearButton_Loaded"' "$PY" -c 'import os,sys; p=sys.argv[1]; s=open(p).read(); open(p,"w").write(s.replace(os.environ["FROM"], os.environ["TO"]))' src/Desk.Client/MainWindow.xaml
git add -A; git commit -qm "feat: wire a handler in XAML"
expect 0 "a XAML change that wires a handler fires config.changed" "config.changed" -- "$E" route show --base "$main" --head 007-xaml-handler
git checkout -q main

if [ "$windows" = 0 ]; then
  # Off Windows: the client cannot be tested here, the server can.
  expect 0 "the Stop gate passes off Windows" "left out off Windows: Desk.Client.Tests, Desk.Client.UiTests" -- "$E" gate run
  if printf '%s' "$LAST_OUTPUT" | grep -q '^build: PASS'; then pass "the client builds off Windows (EnableWindowsTargeting)"; else fail "the build did not pass"; fi
  if printf '%s' "$LAST_OUTPUT" | grep -q '^test: PASS' && printf '%s' "$LAST_OUTPUT" | grep -q 'total 2;'; then
    pass "the server's tests ran"
  else
    fail "the server's tests did not run"; printf '%s\n' "$LAST_OUTPUT" | sed 's/^/     /'
  fi
  "$E" doctor > "$W/doctor.txt" 2>&1
  if grep -q '3 projects target Windows and this is not Windows' "$W/doctor.txt"; then pass "doctor explains the left-out tests"; else fail "doctor does not explain the left-out tests"; sed 's/^/     /' "$W/doctor.txt"; fi
  if [ "$failures" -eq 0 ]; then echo "desktop: all passed"; else echo "desktop: $failures failed"; fi
  exit $((failures > 0))
fi

# On Windows: the Stop gate runs the client's tests (STA included), not the UI tests.
expect 0 "the Stop gate runs the client's tests on Windows" "total 5" -- "$E" gate run --full
if printf '%s' "$LAST_OUTPUT" | grep -q 'left out off Windows'; then fail "the Stop gate left tests out on Windows"; else pass "nothing is left out on Windows"; fi

record=""
judge() { # judge <branch> <exit code>
  rm -rf "$W/evidence"
  expect "$2" "[$1] the judge's verdict" -- "$E" ci --platform github --head "$(git rev-parse "$1")" --base "$main" --branch "$1" --evidence "$W/evidence"
  record="$W/evidence/evidence.json"
}
red() { git add -A; git commit -qm "test($1): red for US1" -m "Lockflight-Red: 1"; }
plain() { git add -A; git commit -qm "$1"; }
spec() { # spec <feature> <requirement line>
  mkdir -p "specs/$1"
  printf '# Feature Specification: %s\n\n## Requirements *(mandatory)*\n\n%s\n' "$1" "$2" > "specs/$1/spec.md"
}
# insert_before <file> <line to find> <text to put before it>, without sed's platform differences.
insert_before() { TEXT="$3" awk -v find="$2" '$0 == find { print ENVIRON["TEXT"] } { print }' "$1" > "$1.new" && mv "$1.new" "$1"; }
replace() { FROM="$2" TO="$3" "$PY" -c 'import os,sys; p=sys.argv[1]; s=open(p).read(); open(p,"w").write(s.replace(os.environ["FROM"], os.environ["TO"]))' "$1"; }

vm=src/Desk.Client/ViewModels/OrderViewModel.cs
discount_test() {
  cat > tests/Desk.Client.Tests/DiscountTests.cs <<'CS'
using Desk.Client.ViewModels;

namespace Desk.Client.Tests;

public class DiscountTests
{
    [Fact]
    [Trait("Requirement", "FR-001")]
    public void TenOrMoreItemsGetTenPercentOff()
    {
        OrderViewModel order = new() { Quantity = 10, UnitPrice = 2m };

        Assert.Equal(18m, order.DiscountedTotal());
    }
}
CS
}
stub='    public decimal DiscountedTotal() => throw new NotImplementedException();
'
impl='    public decimal DiscountedTotal() => Quantity >= 10 ? Total * 0.9m : Total;
'

# 1. An honest ViewModel feature: the test and a stub in a red commit, then the implementation.
git checkout -q -b 001-discount "$main"
spec 001-discount '- **FR-001**: Orders of 10 or more items MUST get 10% off'
insert_before "$vm" "    public void Clear()" "$stub"; discount_test; red 001-discount
replace "$vm" "$stub" "$impl"; plain "feat(001-discount): US1"
judge 001-discount 0
for id in red tests.sinceRed green trace tests.hostTampering gates; do check "$record" "$id" PASS; done

# 2. A stub in code-behind (MainWindow.xaml.cs), with [StaFact] tests that open the window, directly and on its Dispatcher.
git checkout -q -b 002-sta "$main"
spec 002-sta '- **FR-002**: The window title MUST show the item count'
window=src/Desk.Client/MainWindow.xaml.cs
insert_before "$window" "    private void ClearButton_Click(object sender, RoutedEventArgs e)" '    public string ItemTitle() => throw new NotImplementedException();
'
cat > tests/Desk.Client.Tests/WindowTitleTests.cs <<'CS'
namespace Desk.Client.Tests;

public class WindowTitleTests
{
    [StaFact]
    [Trait("Requirement", "FR-002")]
    public void TheTitleShowsTheItemCount()
    {
        MainWindow window = new();
        window.ViewModel.Quantity = 3;

        Assert.Equal("Desk (3 items)", window.ItemTitle());
    }

    [StaFact]
    [Trait("Requirement", "FR-002")]
    public void TheTitleIsComputedOnTheDispatcher()
    {
        MainWindow window = new();
        window.ViewModel.Quantity = 2;

        string title = window.Dispatcher.Invoke(window.ItemTitle);

        Assert.Equal("Desk (2 items)", title);
    }
}
CS
red 002-sta
replace "$window" "    public string ItemTitle() => throw new NotImplementedException();" '    public string ItemTitle() => $"Desk ({ViewModel.Quantity} items)";'
plain "feat(002-sta): US1"
judge 002-sta 0
for id in red green trace tests.hostTampering; do check "$record" "$id" PASS; done
check "$record" red PASS "1 cycles, 2 tests"

# 3. The code first, then its test in a red commit: the test passes before red.
git checkout -q -b 003-test-after "$main"
spec 003-test-after '- **FR-001**: Orders of 10 or more items MUST get 10% off'
insert_before "$vm" "    public void Clear()" "$impl"; plain "feat(003-test-after): US1"
discount_test; red 003-test-after
judge 003-test-after 1
check "$record" red FAIL GREEN_BEFORE

# 4. A server-only feature in the mixed solution: red on an assertion, then green.
git checkout -q -b 004-server "$main"
spec 004-server '- **FR-004**: The price list MUST price a ruler at 0.80'
cat > tests/Desk.Server.Tests/RulerTests.cs <<'CS'
namespace Desk.Server.Tests;

public class RulerTests
{
    [Fact]
    [Trait("Requirement", "FR-004")]
    public void ARulerCostsEightyCents() => Assert.Equal(0.80m, new PriceList().PriceOf("ruler"));
}
CS
red 004-server
replace src/Desk.Server/PriceList.cs '        ["notebook"] = 4.00m,' '        ["notebook"] = 4.00m,
        ["ruler"] = 0.80m,'
plain "feat(004-server): US1"
judge 004-server 0
for id in red green trace gates; do check "$record" "$id" PASS; done

# 5. A [structural] refactor of the ViewModel with a structural test and no red commit. The target branch's CODEOWNERS gives
# the spec an owner, so a person reviews the spec this pull request changes: a structural-only change, red N/A.
git checkout -q main
mkdir -p .github; printf 'specs/** @desk-team\n' > .github/CODEOWNERS
git add -A; git commit -qm "chore: code owners for the specs"; main=$(git rev-parse HEAD)
git checkout -q -b 005-structural "$main"
spec 005-structural '- **FR-010** [structural]: Order totals MUST stay as they are after the change notification is split out'
replace "$vm" '        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(dependent));
    }' '        Notify(name);
        Notify(dependent);
    }

    private void Notify(string? property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));'
cat > tests/Desk.Client.Tests/StructuralTotalsTests.cs <<'CS'
using Desk.Client.ViewModels;

namespace Desk.Client.Tests;

public class StructuralTotalsTests
{
    [Fact]
    [Trait("Requirement", "FR-010")]
    public void TotalsStayAsTheyAre()
    {
        OrderViewModel order = new();
        List<string?> changed = [];
        order.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        order.Quantity = 4;
        order.UnitPrice = 2.5m;

        Assert.Equal(10m, order.Total);
        Assert.Equal(["Quantity", "Total", "UnitPrice", "Total"], changed);
    }
}
CS
plain "refactor(005-structural): split the change notification"
judge 005-structural 0
check "$record" red N/A "structural-only change"
check "$record" trace PASS
check "$record" tests.existingChanged PASS

if [ "$failures" -eq 0 ]; then echo "desktop: all passed"; else echo "desktop: $failures failed"; fi
exit $((failures > 0))
