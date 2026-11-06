#!/usr/bin/env bash
# The fix flow end to end with the published engine, on a copy of samples/dotnet-single whose main holds a planted bug (a discount
# rounded half away from zero, not to even), with no spec and no approvals: a red made with `lockflight red` and a one-line fix
# PASS the local judge with no approvals; a test written after the fix (committed first, or in the same working tree) is
# GREEN_BEFORE for `lockflight red` and for the judge alike; tests that do not compile and a test that fails in its own code are
# rejected with nothing committed; a branch that already adds an endpoint (feature class) is refused, and the same change with a
# hand-typed red and no spec is NOT-EARNED in the judge (trace required, with no spec to trace), which is the boundary; a test edited after its red fails tests since red
# (amend-rejected, amend-weakened); a second cycle gets label 2 and leaves cycle 1's test out, from the cache or re-judged, and a
# rejected cycle is repaired with --redo.
# Usage: ENGINE=<lockflight binary> fix-flow.sh <workdir>
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
E="${ENGINE:?path to the lockflight binary}"
W="$1"; rm -rf "${W:?}"; mkdir -p "$W/repo" "$W/temp"
cp -R "$ROOT/samples/dotnet-single/." "$W/repo/"; rm -rf "$W"/repo/*/*/bin "$W"/repo/*/*/obj
rm -f "$W/repo/tests/Shop.Tests/ContainerSmokeTests.cs"
export LOCKFLIGHT_TEMP="$W/temp"
unset CLAUDECODE SPECIFY_FEATURE SPECIFY_FEATURE_DIRECTORY
failures=0
PY="$(command -v python3 || command -v python)"
EVIDENCE=.lockflight/local/ci/last.json
check() { # check <check id> <verdict>: a check row of the last local judge run
  local got
  got=$("$PY" -c 'import json,sys; d=json.load(open(sys.argv[1])); print(next((c["verdict"] for c in d["checks"] if c["id"]==sys.argv[2]), "none"))' "$EVIDENCE" "$1")
  if [ "$got" = "$2" ]; then echo "ok   $1 is $2"; else echo "FAIL $1 is $got, want $2"; "$PY" -c 'import json,sys; [print("     ", c["id"], c["verdict"], c["why"]) for c in json.load(open(sys.argv[1]))["checks"]]' "$EVIDENCE"; failures=$((failures + 1)); fi
}
check_field() { # check_field <check id> <field> <text>: the row's field (why, reason, requirement) holds the text
  if "$PY" -c 'import json,sys; d=json.load(open(sys.argv[1])); sys.exit(0 if any(c["id"]==sys.argv[2] and sys.argv[4] in c.get(sys.argv[3], "") for c in d["checks"]) else 1)' "$EVIDENCE" "$1" "$2" "$3"
  then echo "ok   $1 $2 holds $3"; else echo "FAIL $1 $2 does not hold $3"; failures=$((failures + 1)); fi
}
out=""
expect_exit() { # expect_exit <code> <what> -- command…  (the output is kept in $out)
  local want="$1" what="$2"; shift 3
  out="$("$@" 2>&1)"; local got=$?
  if [ "$got" = "$want" ]; then echo "ok   $what"; else echo "FAIL $what (exit $got, want $want)"; echo "$out" | sed 's/^/     /'; failures=$((failures + 1)); fi
}
expect_out() { # expect_out <text> <what>: the last command's output holds the text
  if grep -qF -- "$1" <<<"$out"; then echo "ok   $2"; else echo "FAIL $2 (no '$1')"; echo "$out" | sed 's/^/     /'; failures=$((failures + 1)); fi
}
expect_no_out() { # expect_no_out <text> <what>
  if grep -qF -- "$1" <<<"$out"; then echo "FAIL $2 ('$1' is there)"; echo "$out" | sed 's/^/     /'; failures=$((failures + 1)); else echo "ok   $2"; fi
}
same_head() { # same_head <commit> <what>
  if [ "$(git rev-parse HEAD)" = "$1" ]; then echo "ok   $2"; else echo "FAIL $2 (HEAD moved)"; failures=$((failures + 1)); fi
}
red() { "$E" red "$@"; }
judge() { rm -f "$EVIDENCE"; "$E" ci --platform local; }

cd "$W/repo" || exit 1
export GIT_AUTHOR_NAME=dev GIT_AUTHOR_EMAIL=dev@example.com GIT_COMMITTER_NAME=dev GIT_COMMITTER_EMAIL=dev@example.com
git init -q -b main; git config commit.gpgsign false
printf 'version: 1\n' > lockflight.yaml
printf 'bin/\nobj/\n.lockflight/ledger/\n.lockflight/local/\n' > .gitignore
calc=src/Shop.Api/PriceCalculator.cs; tests=tests/Shop.Tests/PriceCalculatorTests.cs
# The planted bug: a discount's midpoint is rounded away from zero, not to even as the doc comment says.
perl -0pi -e 's/(\(100m - percent\) \/ 100m, 2, MidpointRounding\.)ToEven/${1}AwayFromZero/' "$calc"
git add -A; git commit -qm "base with a rounding bug"; main=$(git rev-parse HEAD)
fix_bug() { perl -0pi -e 's/(\(100m - percent\) \/ 100m, 2, MidpointRounding\.)AwayFromZero/${1}ToEven/' "$calc"; }
add_test() { # add_test <C# members>: appended to the test class
  "$PY" - "$tests" "$1" <<'PYEOF'
import sys
path, members = sys.argv[1], sys.argv[2]
text = open(path).read().rstrip()
open(path, "w").write(text[:text.rfind("}")].rstrip() + "\n\n" + members + "\n}\n")
PYEOF
}
ROUNDING='    [Fact]
    public void ApplyDiscount_RoundsMidpointToEven() =>
        Assert.Equal(0.12m, calculator.ApplyDiscount(0.125m, 0m));'
NEGATIVE='    [Fact]
    public void ApplyDiscount_RejectsANegativeAmount() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => calculator.ApplyDiscount(-1m, 10m));'

echo "== 1. a bug fix: lockflight red, then the fix, PASS with no spec and no approvals"
git checkout -q -b fix/discount-rounding "$main"
add_test "$ROUNDING"
expect_exit 0 "lockflight red accepts the reproducing test" -- red
expect_out "Production unchanged since the fork point" "the red says production is unchanged"
expect_out "RED_ASSERT" "the test is RED_ASSERT"
if [ "$(git log -1 --format='%(trailers:key=Lockflight-Red,valueonly)' | tr -d '\r\n')" = 1 ]; then echo "ok   the red commit carries Lockflight-Red: 1"
else echo "FAIL the red commit does not carry Lockflight-Red: 1"; git log -1 | sed 's/^/     /'; failures=$((failures + 1)); fi
red1=$(git rev-parse HEAD)
fix_bug; git commit -qam "fix: round a discount's midpoint to even"
expect_exit 0 "the local judge passes the fix" -- judge
check routing PASS; check_field routing why "class fix"
for id in red tests.sinceRed green; do check "$id" PASS; done
check approvals N/A

echo "== 2. the test written after the fix: GREEN_BEFORE, locally and in the judge"
git checkout -q -b fix/after "$main"
fix_bug; git commit -qam "fix first"
add_test "$ROUNDING"; head=$(git rev-parse HEAD)
expect_exit 1 "lockflight red rejects a test the fix already passes" -- red
expect_out "GREEN_BEFORE" "the red says GREEN_BEFORE"
same_head "$head" "nothing was committed"
git commit -qam "test: red" -m "Lockflight-Red: 1"
expect_exit 1 "the judge fails the same history with a hand-typed trailer" -- judge
check red FAIL; check_field red why GREEN_BEFORE

echo "== 3. the fix and the test in one working tree: GREEN_BEFORE"
git checkout -q -b fix/together "$main"
fix_bug; add_test "$ROUNDING"
expect_exit 1 "lockflight red rejects the test beside its fix" -- red
expect_out "GREEN_BEFORE" "the red says GREEN_BEFORE"
expect_out "Production files in this red" "the red names the production file it holds"
same_head "$main" "nothing was committed"
git checkout -q -- .

echo "== 4. bad reds: COMPILE_RED and RED_IN_TEST, nothing committed"
git checkout -q -b fix/bad "$main"
add_test '    [Fact]
    public void Refund_IsHalf() => Assert.Equal(5m, calculator.Refund(10m));'
expect_exit 1 "tests that do not compile are rejected" -- red
expect_out "COMPILE_RED" "the red says COMPILE_RED"
git checkout -q -- .
add_test '    [Fact]
    public void ApplyDiscount_Throws() { _ = calculator.ApplyDiscount(1m, 1m); throw new InvalidOperationException("broken test"); }'
expect_exit 1 "a test that fails in its own code is rejected" -- red
expect_out "RED_IN_TEST" "the red says RED_IN_TEST"
same_head "$main" "nothing was committed"
git checkout -q -- .

echo "== 5. feature-class work: refused locally, NOT-EARNED in the judge"
ENDPOINT='app.MapGet("/refund", (decimal amount, PriceCalculator calculator) => Results.Ok(new { refund = calculator.Refund(amount) }));'
git checkout -q -b feat/refunds "$main"
ENDPOINT="$ENDPOINT" perl -0pi -e 's/app\.Run\(\);/$ENV{ENDPOINT}\n\napp.Run();/' src/Shop.Api/Program.cs
perl -0pi -e 's/\}\s*\z/\n    \/\/\/ <summary>Half the amount back.<\/summary>\n    public decimal Refund(decimal amount) => amount \/ 2m;\n}\n/' "$calc"
git commit -qam "feat: refunds endpoint"; head=$(git rev-parse HEAD)
add_test '    [Fact]
    public void Refund_IsHalf() => Assert.Equal(5m, calculator.Refund(10m));'
expect_exit 1 "lockflight red refuses a branch that is already feature class with no spec" -- red
expect_out "needs a spec" "the refusal says the change needs a spec"
same_head "$head" "nothing was committed"
git checkout -q -- .
git checkout -q -b feat/refunds-hand "$main"
ENDPOINT="$ENDPOINT" perl -0pi -e 's/app\.Run\(\);/$ENV{ENDPOINT}\n\napp.Run();/' src/Shop.Api/Program.cs
perl -0pi -e 's/\}\s*\z/\n    \/\/\/ <summary>Half the amount back.<\/summary>\n    public decimal Refund(decimal amount) => throw new NotImplementedException();\n}\n/' "$calc"
add_test '    [Fact]
    public void Refund_IsHalf() => Assert.Equal(5m, calculator.Refund(10m));'
git commit -qam "test: refunds" -m "Lockflight-Red: 1"
perl -pi -e 's/=> throw new NotImplementedException\(\);/=> amount \/ 2m;/' "$calc"
git commit -qam "feat: refunds"
expect_exit 5 "the judge holds a feature-class change with no spec at NOT-EARNED" -- judge
check red PASS; check routing PASS; check_field routing why "class feature"
check trace N/A; check_field trace requirement required

echo "== 6. a test edited after its red: tests since red fails"
git checkout -q -b fix/edited "$red1"
perl -0pi -e 's/Assert\.Equal\(0\.12m, calculator\.ApplyDiscount\(0\.125m, 0m\)\)/_ = calculator.ApplyDiscount(0.125m, 0m)/' "$tests"
fix_bug; git commit -qam "fix, and the test no longer asserts"
expect_exit 1 "the judge fails a test whose assertion went after its red" -- judge
check tests.sinceRed FAIL; check_field tests.sinceRed reason AMEND_REJECTED
git checkout -q -b fix/weakened "$main"
add_test '    [Theory]
    [InlineData(0.125, 0.12)]
    [InlineData(0.145, 0.14)]
    public void ApplyDiscount_RoundsEachMidpointToEven(decimal amount, decimal expected) =>
        Assert.Equal(expected, calculator.ApplyDiscount(amount, 0m));'
expect_exit 0 "lockflight red accepts a two-row theory" -- red
perl -0pi -e 's/\r?\n    \[InlineData\(0\.145, 0\.14\)\]//' "$tests"
fix_bug; git commit -qam "fix, and one row less"
expect_exit 1 "the judge fails a theory that lost a row after its red" -- judge
check tests.sinceRed FAIL; check_field tests.sinceRed reason AMEND_WEAKENED

echo "== 7. a second cycle, from the cache and re-judged, and a redo"
git checkout -q -b fix/two "$red1"
add_test "$NEGATIVE"
expect_exit 0 "--dry-run accepts the second bug's test" -- red --dry-run
expect_out "'Lockflight-Red: 2'" "the second red is labelled 2"
expect_no_out "Judging the earlier red" "the earlier red comes from the cache"
expect_no_out "RoundsMidpointToEven" "cycle 1's test is not new again"
same_head "$red1" "a dry run commits nothing"
rm -rf .lockflight/local/cache
expect_exit 0 "with the cache deleted, the second red is still accepted" -- red
expect_out "Judging the earlier red 1" "the earlier red is judged again"
expect_no_out "RED_ASSERT   Shop.Tests.PriceCalculatorTests.ApplyDiscount_RoundsMidpointToEven" "cycle 1's test is not new again"
git reset -q --hard HEAD~1
# A pushed red 2 the judge rejects: its test asserts what the code does already.
add_test '    [Fact]
    public void ApplyDiscount_OfANegativeAmount() =>
        Assert.Equal(-0.9m, calculator.ApplyDiscount(-1m, 10m));'
git commit -qam "test: red 2" -m "Lockflight-Red: 2"
rejected=$(git rev-parse HEAD)
perl -0pi -e 's/ApplyDiscount_OfANegativeAmount\(\) =>\s*Assert\.Equal\(-0\.9m, calculator\.ApplyDiscount\(-1m, 10m\)\);/ApplyDiscount_OfANegativeAmount() =>\n        Assert.Throws<ArgumentOutOfRangeException>(() => calculator.ApplyDiscount(-1m, 10m));/' "$tests"
expect_exit 0 "--redo 2 replaces the rejected red" -- red --redo 2
if [ "$(git log -1 --format='%(trailers:key=Lockflight-Red,valueonly)' | tr -d '\r\n')" = 2 ] && [ "$(git rev-parse HEAD^)" = "$rejected" ]; then echo "ok   the redo is a new commit labelled 2"
else echo "FAIL the redo is not a new commit labelled 2"; failures=$((failures + 1)); fi
fix_bug; perl -0pi -e 's/(public decimal ApplyDiscount\(decimal amount, decimal percent\)\r?\n    \{\r?\n)/${1}        ArgumentOutOfRangeException.ThrowIfNegative(amount);\n/' "$calc"
git commit -qam "fix: rounding and negative amounts"
expect_exit 0 "the judge passes both cycles, the rejected red superseded" -- judge
check red PASS; check green PASS

echo
if [ "$failures" -eq 0 ]; then echo "fix flow: all checks passed"; else echo "fix flow: $failures failed"; exit 1; fi
