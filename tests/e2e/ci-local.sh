#!/usr/bin/env bash
# The local judge gives the CI judge's verdict: on a copy of samples/dotnet-single with the pull request histories of
# ci-judge.sh (a story that passes, a red commit that already holds the implementation, a test weakened after green, a branch
# that changes production code with no red commit), `lockflight ci --platform local` on the checked-out branch and
# `lockflight ci --platform github` with the same head, base and branch agree on the verdict, both status contexts and every
# check's verdict, level and reason code. `ci explain` without an operand then explains the local run.
# Usage: ENGINE=<lockflight binary> ci-local.sh <workdir>
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
E="${ENGINE:?path to the lockflight binary}"
W="$1"; rm -rf "${W:?}"; mkdir -p "$W/repo" "$W/temp"
cp -R "$ROOT/samples/dotnet-single/." "$W/repo/"; rm -rf "$W"/repo/*/*/bin "$W"/repo/*/*/obj
rm -f "$W/repo/tests/Shop.Tests/ContainerSmokeTests.cs"
export LOCKFLIGHT_TEMP="$W/temp"
failures=0
PY="$(command -v python3 || command -v python)"
cd "$W/repo" || exit 1
export GIT_AUTHOR_NAME=dev GIT_AUTHOR_EMAIL=dev@example.com GIT_COMMITTER_NAME=dev GIT_COMMITTER_EMAIL=dev@example.com
git init -q -b main; git config commit.gpgsign false
printf 'version: 2\ngates:\n  quality:\n    suppressions: report\n  docs:\n    references: report\napprovals:\n  check: report\n' > lockflight.yaml
printf 'bin/\nobj/\n.lockflight/ledger/\n.lockflight/local/\n' > .gitignore
git add -A; git commit -qm base; main=$(git rev-parse HEAD)
calc=src/Shop.Api/PriceCalculator.cs; tests=tests/Shop.Tests/PriceCalculatorTests.cs

# The verdict, the contexts and each check as one line per fact, for a comparison that names what differs.
facts() {
  "$PY" -c '
import json, sys
d = json.load(open(sys.argv[1]))
print("verdict", d["verdict"])
for name, verdict in sorted(d["contexts"].items()): print("context", name, verdict)
for c in sorted(d["checks"], key=lambda c: c["id"]): print("check", c["id"], c["verdict"], c["requirement"], c.get("reason", "-"))
' "$1"
}

# same <case> <branch> <want verdict>: judges the checked-out branch both ways and compares.
same() {
  local case="$1" branch="$2" want="$3" head
  head=$(git rev-parse HEAD)
  rm -rf "$W/evidence" .lockflight/local/ci
  "$E" ci --platform github --head "$head" --base "$main" --branch "$branch" --evidence "$W/evidence" > "$W/ci.log" 2>&1
  local ci=$?
  "$E" ci --platform local > "$W/local.log" 2>&1
  local local_exit=$?
  if [ ! -f "$W/evidence/evidence.json" ] || [ ! -f .lockflight/local/ci/last.json ]; then
    echo "FAIL $case: a judge wrote no evidence (CI exit $ci, local exit $local_exit)"; sed 's/^/     /' "$W/ci.log" "$W/local.log"; failures=$((failures + 1)); return
  fi
  facts "$W/evidence/evidence.json" > "$W/ci.facts"; facts .lockflight/local/ci/last.json > "$W/local.facts"
  if [ "$ci" = "$local_exit" ] && cmp -s "$W/ci.facts" "$W/local.facts"; then echo "ok   $case: the local verdict and reasons equal CI's (exit $ci)"
  else echo "FAIL $case: the local judge differs from CI (exit $local_exit, CI exit $ci)"; diff "$W/ci.facts" "$W/local.facts" | sed 's/^/     /'; failures=$((failures + 1)); fi
  if grep -qx "verdict $want" "$W/ci.facts"; then echo "ok   $case: CI says $want"
  else echo "FAIL $case: CI does not say $want"; sed 's/^/     /' "$W/ci.facts"; failures=$((failures + 1)); fi
}

git checkout -q -b 001-shipping
mkdir -p specs/001-shipping
printf '# Feature Specification: Shipping\n\n## Requirements *(mandatory)*\n\n- **FR-001**: System MUST charge 5.00 for the first kilogram\n' > specs/001-shipping/spec.md
perl -0pi -e 's/\}\s*\z/\n    public decimal ShippingCost(decimal weightKg) => throw new NotImplementedException();\n}\n/' "$calc"
perl -0pi -e 's/\}\s*\z/\n    [Fact]\n    [Trait("Requirement", "FR-001")]\n    public void TheFirstKilogramCostsFive() => Assert.Equal(5m, new PriceCalculator().ShippingCost(1m));\n}\n/' "$tests"
git add -A; git commit -qm "test(001-shipping): red for US1" -m "Lockflight-Red: 1"
perl -pi -e 's/=> throw new NotImplementedException\(\);/=> 5m;/' "$calc"
git commit -qam "feat(001-shipping): US1"; story=$(git rev-parse HEAD)
same "a story that passes" 001-shipping PASS
explained="$("$E" ci explain)"; code=$?
if [ "$code" = 0 ] && grep -q "the local judge's evidence" <<< "$explained" && grep -q "^Nothing to explain" <<< "$explained"; then echo "ok   ci explain reads the local run"
else echo "FAIL ci explain on the local run (exit $code)"; sed 's/^/     /' <<< "$explained"; failures=$((failures + 1)); fi

git checkout -q -b 002-cheat "$main"
perl -0pi -e 's/\}\s*\z/\n    public decimal ShippingCost(decimal weightKg) => 5m;\n}\n/' "$calc"
perl -0pi -e 's/\}\s*\z/\n    [Fact]\n    public void TheFirstKilogramCostsFive() => Assert.Equal(5m, new PriceCalculator().ShippingCost(1m));\n}\n/' "$tests"
git add -A; git commit -qm "test: red" -m "Lockflight-Red: 1"
same "a red commit holding the implementation" 002-cheat FAIL
explained="$("$E" ci explain)"
if grep -qx '  Reason: GREEN_BEFORE' <<< "$explained"; then echo "ok   ci explain names GREEN_BEFORE for the local run"
else echo "FAIL ci explain does not name GREEN_BEFORE"; sed 's/^/     /' <<< "$explained"; failures=$((failures + 1)); fi

git checkout -q -b 003-weakened "$story"
perl -pi -e 's/Assert.Equal\(5m, new PriceCalculator\(\).ShippingCost\(1m\)\)/Assert.Equal(5m, 5m)/' "$tests"
git commit -qam "test: simpler"
same "a test weakened after green" 003-weakened FAIL

git checkout -q -b 004-no-red "$main"
perl -0pi -e 's/\}\s*\z/\n    public decimal Handling() => 1m;\n}\n/' "$calc"
git commit -qam "feat: handling"
same "production code without a red commit" 004-no-red NOT-EARNED

if [ "$failures" -gt 0 ]; then echo "$failures local judge checks failed"; exit 1; fi
echo "Local judge end to end: all checks passed"
