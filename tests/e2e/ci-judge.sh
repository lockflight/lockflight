#!/usr/bin/env bash
# The CI judge end to end with the published engine, on a copy of samples/dotnet-single with a scripted pull request history: a
# story whose red commit fails in production code and whose implementation passes is judged PASS on red, tests since red,
# green and trace, and the report sets each status context; a red commit that already holds the implementation FAILs red; a
# test weakened after green FAILs tests since red; a missing judge artifact is NOT-EARNED.
# Usage: ENGINE=<lockflight binary> ci-judge.sh <workdir>
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
E="${ENGINE:?path to the lockflight binary}"
W="$1"; rm -rf "${W:?}"; mkdir -p "$W/repo" "$W/temp"
cp -R "$ROOT/samples/dotnet-single/." "$W/repo/"; rm -rf "$W"/repo/*/*/bin "$W"/repo/*/*/obj
rm -f "$W/repo/tests/Shop.Tests/ContainerSmokeTests.cs"
export LOCKFLIGHT_TEMP="$W/temp"
failures=0
PY="$(command -v python3 || command -v python)"
check() { # check <evidence or verdict file> <check id> <verdict>
  local got
  got=$("$PY" -c 'import json,sys; d=json.load(open(sys.argv[1])); print(next((c["verdict"] for c in d["checks"] if c["id"]==sys.argv[2]), "none"))' "$1" "$2")
  if [ "$got" = "$3" ]; then echo "ok   $2 is $3"; else echo "FAIL $2 is $got, want $3"; "$PY" -c 'import json,sys; [print("     ", c["id"], c["verdict"], c["why"]) for c in json.load(open(sys.argv[1]))["checks"]]' "$1"; failures=$((failures + 1)); fi
}
expect_exit() { # expect_exit <code> <what> -- command…
  local want="$1" what="$2"; shift 3
  local out; out="$("$@" 2>&1)"; local got=$?
  if [ "$got" = "$want" ]; then echo "ok   $what"; else echo "FAIL $what (exit $got, want $want)"; echo "$out" | sed 's/^/     /'; failures=$((failures + 1)); fi
}
cd "$W/repo" || exit 1
export GIT_AUTHOR_NAME=dev GIT_AUTHOR_EMAIL=dev@example.com GIT_COMMITTER_NAME=dev GIT_COMMITTER_EMAIL=dev@example.com
git init -q -b main; git config commit.gpgsign false
printf 'version: 1\n' > lockflight.yaml
printf 'bin/\nobj/\n.lockflight/ledger/\n.lockflight/local/\n' > .gitignore
git add -A; git commit -qm base; main=$(git rev-parse HEAD)
calc=src/Shop.Api/PriceCalculator.cs; tests=tests/Shop.Tests/PriceCalculatorTests.cs
git checkout -q -b 001-shipping
mkdir -p specs/001-shipping
printf '# Feature Specification: Shipping\n\n## Requirements *(mandatory)*\n\n- **FR-001**: System MUST charge 5.00 for the first kilogram\n' > specs/001-shipping/spec.md
perl -0pi -e 's/\}\s*\z/\n    public decimal ShippingCost(decimal weightKg) => throw new NotImplementedException();\n}\n/' "$calc"
perl -0pi -e 's/\}\s*\z/\n    [Fact]\n    [Trait("Requirement", "FR-001")]\n    public void TheFirstKilogramCostsFive() => Assert.Equal(5m, new PriceCalculator().ShippingCost(1m));\n}\n/' "$tests"
git add -A; git commit -qm "test(001-shipping): red for US1" -m "Lockflight-Red: 1"
perl -pi -e 's/=> throw new NotImplementedException\(\);/=> 5m;/' "$calc"
git commit -qam "feat(001-shipping): US1"; head=$(git rev-parse HEAD)

judge() { "$E" ci --platform github --head "$1" --base "$main" --branch 001-shipping --evidence "$W/evidence"; }
judge "$head" > "$W/judge.log" 2>&1; echo "     judge exit $? ($(head -n 1 "$W/judge.log"))"
for id in red tests.sinceRed green trace tests.hostTampering; do check "$W/evidence/evidence.json" "$id" PASS; done
expect_exit 0 "the lockflight/judge context passes" -- "$E" ci report --evidence "$W/evidence" --head "$head" --out "$W/verdict" --context lockflight/judge
if grep -q '^<!-- lockflight:ci:v1 head=' "$W/verdict/comment.md"; then echo "ok   the comment starts with its marker"; else echo "FAIL the comment has no marker"; failures=$((failures + 1)); fi
expect_exit 5 "a missing judge artifact is NOT-EARNED" -- "$E" ci report --evidence "$W/nothing" --head "$head" --out "$W/missing" --context gates

git checkout -q -b 002-cheat "$main"
perl -0pi -e 's/\}\s*\z/\n    public decimal ShippingCost(decimal weightKg) => 5m;\n}\n/' "$calc"
perl -0pi -e 's/\}\s*\z/\n    [Fact]\n    public void TheFirstKilogramCostsFive() => Assert.Equal(5m, new PriceCalculator().ShippingCost(1m));\n}\n/' "$tests"
git add -A; git commit -qm "test: red" -m "Lockflight-Red: 1"
judge "$(git rev-parse HEAD)" > "$W/cheat.log" 2>&1
check "$W/evidence/evidence.json" red FAIL

git checkout -q 001-shipping
perl -pi -e 's/Assert.Equal\(5m, new PriceCalculator\(\).ShippingCost\(1m\)\)/Assert.Equal(5m, 5m)/' "$tests"
git commit -qam "test: simpler"
judge "$(git rev-parse HEAD)" > "$W/weakened.log" 2>&1
check "$W/evidence/evidence.json" tests.sinceRed FAIL

if [ "$failures" -gt 0 ]; then echo "$failures CI judge checks failed"; exit 1; fi
echo "CI judge end to end: all checks passed"
