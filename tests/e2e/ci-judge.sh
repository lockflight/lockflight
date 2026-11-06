#!/usr/bin/env bash
# The CI judge end to end with the published engine, on a copy of samples/dotnet-single with a scripted pull request history: a
# story whose red commit fails in production code and whose implementation passes is judged PASS on red, tests since red,
# green and trace, and the report passes both status contexts (gates and lockflight/judge); a red commit that already holds the implementation FAILs red; a
# test weakened after green FAILs tests since red; a missing judge artifact is NOT-EARNED. The same story on a copy of
# samples/dotnet-mstest passes red, green and trace on MSTest. A pull request whose test project replaces the gitleaks binary
# during its build, and adds a secret, still FAILs the gates: the judge scans for secrets before it builds anything, and a later
# run of the replaced binary is refused by its hash. No build server (compiler, Razor, MSBuild) the judge started outlives it.
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

# Each run starts from an empty evidence folder, so a run that writes none is never judged on an earlier run's evidence.
judge() { rm -rf "$W/evidence"; "$E" ci --platform github --head "$1" --base "$main" --branch "${2:-001-shipping}" --evidence "$W/evidence"; }
# The process ids of .NET build servers: the compiler server, the Razor build server, and MSBuild nodes kept for reuse or the
# MSBuild server (worker nodes started without reuse exit on their own).
build_servers() {
  if [ "$(uname -o 2>/dev/null)" = Msys ] || [ "$(uname -o 2>/dev/null)" = Cygwin ]; then
    powershell.exe -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { \$_.Name -ne 'powershell.exe' -and \$_.CommandLine -match 'VBCSCompiler|rzc\.dll|nodemode:8|nodeReuse:true' } | ForEach-Object { \$_.ProcessId }" | tr -d '\r' | sort
  else
    pgrep -f 'VBCSCompiler|rzc\.dll|nodemode:8|nodeReuse:true' | sort
  fi
}
describe_processes() { # describe_processes "<pid> <pid>…"
  if [ "$(uname -o 2>/dev/null)" = Msys ] || [ "$(uname -o 2>/dev/null)" = Cygwin ]; then
    powershell.exe -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { @(${1// /,}) -contains \$_.ProcessId } | ForEach-Object { '{0} {1}' -f \$_.ProcessId, \$_.CommandLine }" | tr -d '\r' | sed 's/^/     /'
  else
    ps -o pid,args -p "${1// /,}" 2>/dev/null | sed 's/^/     /'
  fi
}
dotnet build-server shutdown > /dev/null 2>&1
servers_before=$(build_servers)
expect_exit 0 "the judge passes the story" -- judge "$head"
left_running() { comm -13 <(printf '%s\n' "$servers_before") <(build_servers) | grep . | paste -sd' ' -; }
left=$(left_running)
for _ in 1 2 3 4 5; do [ -z "$left" ] && break; sleep 2; left=$(left_running); done
if [ -z "$left" ]; then echo "ok   no build server the judge started is left running"
else echo "FAIL build servers left running after the judge: $left"; describe_processes "$left"; failures=$((failures + 1)); fi
for id in red tests.sinceRed green trace tests.hostTampering; do check "$W/evidence/evidence.json" "$id" PASS; done
expect_exit 0 "the lockflight/judge context passes" -- "$E" ci report --evidence "$W/evidence" --head "$head" --out "$W/verdict" --context lockflight/judge
expect_exit 0 "the gates context passes" -- "$E" ci report --evidence "$W/evidence" --head "$head" --out "$W/verdict-gates" --context gates
if grep -q '^<!-- lockflight:ci:v1 head=' "$W/verdict/comment.md"; then echo "ok   the comment starts with its marker"; else echo "FAIL the comment has no marker"; failures=$((failures + 1)); fi
expect_exit 5 "a missing judge artifact is NOT-EARNED" -- "$E" ci report --evidence "$W/nothing" --head "$head" --out "$W/missing" --context gates

git checkout -q -b 002-cheat "$main"
perl -0pi -e 's/\}\s*\z/\n    public decimal ShippingCost(decimal weightKg) => 5m;\n}\n/' "$calc"
perl -0pi -e 's/\}\s*\z/\n    [Fact]\n    public void TheFirstKilogramCostsFive() => Assert.Equal(5m, new PriceCalculator().ShippingCost(1m));\n}\n/' "$tests"
git add -A; git commit -qm "test: red" -m "Lockflight-Red: 1"
expect_exit 1 "a red commit holding the implementation fails the judge" -- judge "$(git rev-parse HEAD)"
check "$W/evidence/evidence.json" red FAIL

git checkout -q 001-shipping
perl -pi -e 's/Assert.Equal\(5m, new PriceCalculator\(\).ShippingCost\(1m\)\)/Assert.Equal(5m, 5m)/' "$tests"
git commit -qam "test: simpler"
expect_exit 1 "a test weakened after green fails the judge" -- judge "$(git rev-parse HEAD)"
check "$W/evidence/evidence.json" tests.sinceRed FAIL

# A pull request whose test project replaces the gitleaks binary during the build with one that finds nothing, and adds a
# secret. The judge scans before it builds, so the secret FAILs the gates; a gate run after the swap refuses the replaced binary.
if [ -n "${LOCKFLIGHT_GITLEAKS:-}" ] && [ -f "$LOCKFLIGHT_GITLEAKS" ]; then
  ext=""; case "$LOCKFLIGHT_GITLEAKS" in *.exe) ext=.exe ;; esac
  mkdir -p "$W/tools"; gitleaks="$W/tools/gitleaks$ext"; cp "$LOCKFLIGHT_GITLEAKS" "$gitleaks"; chmod 755 "$gitleaks"
  sum=$( (sha256sum < "$gitleaks" 2>/dev/null || shasum -a 256 < "$gitleaks") | cut -d' ' -f1)
  git checkout -q -b 003-blind "$head"
  if [ -n "$ext" ]; then cp /usr/bin/true.exe "tests/Shop.Tests/fake-gitleaks$ext"
  else printf '#!/bin/sh\nexit 0\n' > tests/Shop.Tests/fake-gitleaks; chmod +x tests/Shop.Tests/fake-gitleaks; fi
  perl -0pi -e "s#</Project>#  <Target Name=\"Blind\" BeforeTargets=\"Build\" Condition=\"'\\\$(LOCKFLIGHT_GITLEAKS)' != ''\">\n    <Copy SourceFiles=\"fake-gitleaks$ext\" DestinationFiles=\"\\\$(LOCKFLIGHT_GITLEAKS)\" />\n  </Target>\n</Project>#" tests/Shop.Tests/Shop.Tests.csproj
  # A Slack bot token shape gitleaks recognises; not a real token, and split so this script itself does not match.
  mkdir -p config; printf 'slack = "%s"\n' "xo""xb-123456789012-1234567890123-AbCdEfGhIjKlMnOpQrStUvWx" > config/settings.txt
  git add -A; git commit -qm "chore: settings"
  LOCKFLIGHT_GITLEAKS="$gitleaks" LOCKFLIGHT_GITLEAKS_SHA256="$sum" expect_exit 1 "a secret fails the gates although the build replaces gitleaks" -- judge "$(git rev-parse HEAD)" 003-blind
  check "$W/evidence/evidence.json" gates FAIL
  if cmp -s "$gitleaks" "tests/Shop.Tests/fake-gitleaks$ext"; then echo "ok   the pull request's build replaced gitleaks"
  else echo "FAIL the pull request's build did not replace gitleaks, so the order was not tested"; failures=$((failures + 1)); fi
  if "$PY" -c 'import json,sys; s=[x for x in json.load(open(sys.argv[1]))["steps"] if x["id"]=="security.secrets"]; sys.exit(0 if s and s[0]["verdict"]=="FAIL" else 1)' "$W/evidence/gates-result.json"
  then echo "ok   security.secrets is FAIL"; else echo "FAIL security.secrets is not FAIL"; cat "$W/evidence/gates-result.json"; echo; failures=$((failures + 1)); fi
  LOCKFLIGHT_GITLEAKS="$gitleaks" LOCKFLIGHT_GITLEAKS_SHA256="$sum" expect_exit 3 "a gate run refuses the replaced gitleaks by its hash" -- "$E" gate run --profile pr --base "$main"
fi

# MSTest: the judge drives red/green on MSTest as on xUnit.
mkdir -p "$W/mstest"; cp -R "$ROOT/samples/dotnet-mstest/." "$W/mstest/"; rm -rf "$W"/mstest/*/*/bin "$W"/mstest/*/*/obj
cd "$W/mstest" || exit 1
git init -q -b main; git config commit.gpgsign false
printf 'version: 1\n' > lockflight.yaml
printf 'bin/\nobj/\n.lockflight/ledger/\n.lockflight/local/\n' > .gitignore
git add -A; git commit -qm base; main=$(git rev-parse HEAD)
git checkout -q -b 001-lending
mkdir -p specs/001-lending
printf '# Feature Specification: Lending\n\n## Requirements *(mandatory)*\n\n- **FR-003**: System MUST lend an available copy\n' > specs/001-lending/spec.md
perl -0pi -e 's/\}\s*\z/\n    public bool Lend(string title) => throw new NotImplementedException();\n}\n/' src/Lending/Catalog.cs
perl -0pi -e 's/\}\s*\z/\n    [TestMethod]\n    [TestProperty("Requirement", "FR-003")]\n    public void AnAvailableCopyIsLent()\n    {\n        Catalog catalog = new();\n        catalog.AddCopies("Dune", 1);\n\n        Assert.IsTrue(catalog.Lend("Dune"));\n    }\n}\n/' tests/Lending.Tests/CatalogTests.cs
git add -A; git commit -qm "test(001-lending): red for US1" -m "Lockflight-Red: 1"
perl -pi -e 's/=> throw new NotImplementedException\(\);/=> Copies(title) > 0;/' src/Lending/Catalog.cs
git commit -qam "feat(001-lending): US1"; head=$(git rev-parse HEAD)
expect_exit 0 "the judge passes the story on MSTest" -- judge "$head" 001-lending
for id in red tests.sinceRed green trace tests.hostTampering; do check "$W/evidence/evidence.json" "$id" PASS; done
expect_exit 0 "the lockflight/judge context passes on MSTest" -- "$E" ci report --evidence "$W/evidence" --head "$head" --out "$W/verdict-mstest" --context lockflight/judge
expect_exit 0 "the gates context passes on MSTest" -- "$E" ci report --evidence "$W/evidence" --head "$head" --out "$W/verdict-mstest-gates" --context gates

if [ "$failures" -gt 0 ]; then echo "$failures CI judge checks failed"; exit 1; fi
echo "CI judge end to end: all checks passed"
