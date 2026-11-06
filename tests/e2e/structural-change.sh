#!/usr/bin/env bash
# A structural-only change in the CI judge end to end, with the published engine, on a copy of samples/dotnet-single: a pull
# request whose spec is all [structural] needs no red commit when its spec is changed under the target branch's CODEOWNERS.
# A refactor with a characterisation test PASSes with red N/A, and the report, deciding the waiver again from git, passes it;
# the same refactor on a branch without CODEOWNERS is RED_MISSING. Behaviour changes dressed as structural do not pass: one
# whose characterisation test pins the old output FAILs trace, one with a new plain test is NOT-EARNED on trace, one an
# unchanged existing test catches FAILs green, and one whose existing test is weakened FAILs tests.existingChanged (its old
# version runs on the new code). A behaviour change no test pins, beside a structural test that asserts little, passes: the
# residual the comment says is accepted by review.
# Usage: ENGINE=<lockflight binary> structural-change.sh <workdir>
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
E="${ENGINE:?path to the lockflight binary}"
W="$1"; rm -rf "${W:?}"; mkdir -p "$W/repo" "$W/temp"
cp -R "$ROOT/samples/dotnet-single/." "$W/repo/"; rm -rf "$W"/repo/*/*/bin "$W"/repo/*/*/obj
rm -f "$W/repo/tests/Shop.Tests/ContainerSmokeTests.cs"
export LOCKFLIGHT_TEMP="$W/temp"
failures=0
PY="$(command -v python3 || command -v python)"
check() { # check <evidence file> <check id> <verdict>
  local got
  got=$("$PY" -c 'import json,sys; d=json.load(open(sys.argv[1])); print(next((c["verdict"] for c in d["checks"] if c["id"]==sys.argv[2]), "none"))' "$1" "$2")
  if [ "$got" = "$3" ]; then echo "ok   $2 is $3"; else echo "FAIL $2 is $got, want $3"; "$PY" -c 'import json,sys; [print("     ", c["id"], c["verdict"], c["why"]) for c in json.load(open(sys.argv[1]))["checks"]]' "$1"; failures=$((failures + 1)); fi
}
check_reason() { # check_reason <evidence file> <check id> <reason>
  local got
  got=$("$PY" -c 'import json,sys; d=json.load(open(sys.argv[1])); print(next((c.get("reason","none") for c in d["checks"] if c["id"]==sys.argv[2]), "none"))' "$1" "$2")
  if [ "$got" = "$3" ]; then echo "ok   $2 names $3"; else echo "FAIL $2 names $got, want $3"; failures=$((failures + 1)); fi
}
expect_exit() { # expect_exit <code> <what> -- command…
  local want="$1" what="$2"; shift 3
  local out; out="$("$@" 2>&1)"; local got=$?
  if [ "$got" = "$want" ]; then echo "ok   $what"; else echo "FAIL $what (exit $got, want $want)"; echo "$out" | sed 's/^/     /'; failures=$((failures + 1)); fi
}
contains() { # contains <file> <text> <what>
  if grep -qF -- "$2" "$1"; then echo "ok   $3"; else echo "FAIL $3: '$2' not in $1"; failures=$((failures + 1)); fi
}
cd "$W/repo" || exit 1
export GIT_AUTHOR_NAME=dev GIT_AUTHOR_EMAIL=dev@example.com GIT_COMMITTER_NAME=dev GIT_COMMITTER_EMAIL=dev@example.com
git init -q -b main; git config commit.gpgsign false
printf 'version: 1\n' > lockflight.yaml
printf 'bin/\nobj/\n.lockflight/ledger/\n.lockflight/local/\n' > .gitignore
git add -A; git commit -qm base; bare=$(git rev-parse HEAD)
mkdir -p .github; printf 'specs/** @shop-team\n' > .github/CODEOWNERS
git add -A; git commit -qm "chore: code owners for the specs"; main=$(git rev-parse HEAD)
calc=src/Shop.Api/PriceCalculator.cs; tests=tests/Shop.Tests/PriceCalculatorTests.cs; structural=tests/Shop.Tests/RoundingStructureTests.cs

# The feature: the rounding to cents moves into one helper, and the totals must stay as they are.
spec() {
  mkdir -p specs/012-rounding
  printf '# Feature Specification: Rounding helper\n\n## Requirements *(mandatory)*\n\n- **FR-010** [structural]: Line totals and discounts MUST round to cents as before after the rounding moves into one helper\n' > specs/012-rounding/spec.md
}
refactor() {
  perl -0pi -e 's/return Math\.Round\(unitPrice \* quantity, 2, MidpointRounding\.ToEven\);/return Cents(unitPrice * quantity);/; s/return Math\.Round\(amount \* \(100m - percent\) \/ 100m, 2, MidpointRounding\.ToEven\);/return Cents(amount * (100m - percent) \/ 100m);/; s/\}\s*\z/\n    private static decimal Cents(decimal value) => Math.Round(value, 2, MidpointRounding.ToEven);\n}\n/' "$calc"
}
# A characterisation test of FR-010 in a file of its own: <expected> for LineTotal(0.0625, 2), or <call> <expected> given.
characterise() { # characterise <call> <expected>
  cat > "$structural" <<EOF
using Shop.Api;

namespace Shop.Tests;

public sealed class RoundingStructureTests
{
    [Fact]
    [Trait("Requirement", "FR-010")]
    public void TotalsRoundToCentsAsBefore() =>
        Assert.Equal($2, new PriceCalculator().$1);
}
EOF
}
round_away() { perl -0pi -e 's/MidpointRounding\.ToEven\);\n    \}\n\n    \/\/\/ <summary>Applies/MidpointRounding.AwayFromZero);\n    }\n\n    \/\/\/ <summary>Applies/' "$calc"; }
judge() { rm -rf "$W/evidence"; "$E" ci --platform github --head "$1" --base "${3:-$main}" --branch "$2" --evidence "$W/evidence"; }

# 1. A refactor with a characterisation test, its spec changed under CODEOWNERS: PASS without a red commit.
git checkout -q -b 012-rounding "$main"
spec; refactor; characterise "LineTotal(0.0625m, 2)" "0.12m"
git add -A; git commit -qm "refactor(012-rounding): one rounding helper"; head=$(git rev-parse HEAD)
expect_exit 0 "a structural-only refactor passes without a red commit" -- judge "$head" 012-rounding
check "$W/evidence/evidence.json" red N/A
check "$W/evidence/evidence.json" tests.sinceRed N/A
check "$W/evidence/evidence.json" trace PASS
check "$W/evidence/evidence.json" tests.existingChanged PASS
check "$W/evidence/evidence.json" green PASS
expect_exit 0 "the report decides the waiver again from git and passes" -- "$E" ci report --evidence "$W/evidence" --head "$head" --base "$main" --branch 012-rounding --event pr --platform github --out "$W/verdict"
contains "$W/verdict/comment.md" "Structural-only change — no red commit required." "the comment opens with the structural-only change"
contains "$W/verdict/comment.md" "Accepted by review, not proved" "the comment says what review accepts"
"$PY" -c 'import json,sys; d=json.load(open(sys.argv[1])); d["structuralChange"]=None; [c.update(requirement="off", verdict="N/A") for c in d["checks"] if c["id"]=="trace"]; json.dump(d, open(sys.argv[1],"w"))' "$W/evidence/evidence.json"
expect_exit 3 "the report refuses evidence that turns trace off in a structural-only change" -- "$E" ci report --evidence "$W/evidence" --head "$head" --base "$main" --branch 012-rounding --event pr --platform github --out "$W/verdict-forged"

# 2. The same refactor on a target branch without CODEOWNERS: no person gated the spec, so the red is still missing.
git checkout -q -b bare/012-rounding "$bare"
spec; refactor; characterise "LineTotal(0.0625m, 2)" "0.12m"
git add -A; git commit -qm "refactor(012-rounding): one rounding helper"
expect_exit 5 "without CODEOWNERS the structural spec still needs a red" -- judge "$(git rev-parse HEAD)" bare/012-rounding "$bare"
check "$W/evidence/evidence.json" red NOT-EARNED
check_reason "$W/evidence/evidence.json" red RED_MISSING
contains "$W/evidence/evidence.json" "no CODEOWNERS file" "RED_MISSING names the unmet condition"

# 3. A behaviour change whose characterisation test pins the old output: trace FAILs.
git checkout -q -b c3/012-rounding "$main"
spec; round_away; characterise "LineTotal(0.0625m, 2)" "0.12m"
git add -A; git commit -qm "refactor(012-rounding): rounding"
expect_exit 1 "a behaviour change its characterisation test pins fails" -- judge "$(git rev-parse HEAD)" c3/012-rounding
check "$W/evidence/evidence.json" trace FAIL

# 4. A refactor beside new behaviour and a new plain test for it: trace is NOT-EARNED, the new test needs a red.
git checkout -q -b c4/012-rounding "$main"
spec; refactor; characterise "LineTotal(0.0625m, 2)" "0.12m"
perl -0pi -e 's/\}\s*\z/\n    \/\/\/ <summary>Flat shipping per order.<\/summary>\n    public decimal Shipping() => 5m;\n}\n/' "$calc"
perl -0pi -e 's/\}\s*\z/\n    [Fact]\n    public void Shipping_IsFlat() =>\n        Assert.Equal(5m, calculator.Shipping());\n}\n/' "$tests"
git add -A; git commit -qm "refactor(012-rounding): helper and shipping"
expect_exit 5 "a new plain test in a structural-only change is not earned" -- judge "$(git rev-parse HEAD)" c4/012-rounding
check "$W/evidence/evidence.json" trace NOT-EARNED
check_reason "$W/evidence/evidence.json" trace STRUCTURAL_CHANGE_NEW_TEST

# 5. A behaviour change an unchanged existing test catches: green FAILs.
git checkout -q -b c5/012-rounding "$main"
spec; round_away; characterise "LineTotal(8.50m, 3)" "25.50m"
git add -A; git commit -qm "refactor(012-rounding): rounding"
expect_exit 1 "a behaviour change an existing test catches fails" -- judge "$(git rev-parse HEAD)" c5/012-rounding
check "$W/evidence/evidence.json" green FAIL

# 6. The same behaviour change with that existing test weakened to accept it: its old version FAILs on the new code.
git checkout -q -b c6/012-rounding "$main"
spec; round_away; characterise "LineTotal(8.50m, 3)" "25.50m"
perl -0pi -e 's/Assert\.Equal\(0\.12m, calculator\.LineTotal\(0\.0625m, 2\)\)/Assert.True(calculator.LineTotal(0.0625m, 2) > 0m)/' "$tests"
git add -A; git commit -qm "refactor(012-rounding): rounding"
expect_exit 1 "an existing test weakened to accept a behaviour change fails" -- judge "$(git rev-parse HEAD)" c6/012-rounding
check "$W/evidence/evidence.json" tests.existingChanged FAIL
check_reason "$W/evidence/evidence.json" tests.existingChanged EXISTING_TEST_OLD_VERSION_FAILS

# 7. The residual: a behaviour change no test pins, beside a structural test that asserts little, passes. The spec's review
# is what stands against it, and the comment says the claim is accepted by review, not proved.
git checkout -q -b c7/012-rounding "$main"
spec
perl -0pi -e 's/\(100m - percent\) \/ 100m, 2, MidpointRounding\.ToEven\)/(100m - percent) \/ 100m, 2, MidpointRounding.AwayFromZero)/' "$calc"
characterise "ApplyDiscount(100m, 10m)" "90m"
git add -A; git commit -qm "refactor(012-rounding): rounding"; head=$(git rev-parse HEAD)
expect_exit 0 "a behaviour change no test pins passes, as the guide says" -- judge "$head" c7/012-rounding
expect_exit 0 "its report passes too" -- "$E" ci report --evidence "$W/evidence" --head "$head" --base "$main" --branch c7/012-rounding --event pr --platform github --out "$W/verdict-c7"
contains "$W/verdict-c7/comment.md" "Accepted by review, not proved" "the comment leaves the claim to review"

if [ "$failures" -gt 0 ]; then echo "$failures structural-change checks failed"; exit 1; fi
echo "Structural-only change end to end: all checks passed"
