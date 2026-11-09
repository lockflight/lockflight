#!/usr/bin/env bash
# Red-team cases against the CI judge itself, with the published engine, on a copy of samples/dotnet-single: each pull request
# plants attacks that hold today without isolating the pull request's code, the judge runs as the action runs it, and each case
# checks the outcome and that the guide states the same behaviour.
#   - a package whose build targets compile a module initializer into the test assembly, and a module initializer in the test
#     code: the host-tampering scan reads the assembly that runs and finds both (NOT-EARNED);
#   - git hooks, a filter and git settings committed in the pull request: none of them runs;
#   - Directory.Build.props turning warnings-as-errors and analyzers off, and the workflow's action pin moved to another
#     commit: both are listed as protected changes (policy.protected), which CODEOWNERS protects;
#   - every comparable key of lockflight.yaml loosened: the policy check FAILs and names each one; the keys it does not
#     compare (time limits, ci.redCommits) are not named, as the guide says;
#   - a requirement the base already had marked [outside .NET], and a [structural] requirement with a test that asserts what
#     holds on both sides: trace passes, and the comment calls both out for review;
#   - an implementation that hangs at head after a fast red: the test run is stopped at the time limit and the gates are ERROR
#     with STEP_TIMED_OUT.
# Known gaps are listed at the end with the guide's entry for each.
# Usage: ENGINE=<lockflight binary> judge.sh <workdir>
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
# shellcheck source=tests/redteam/lib.sh
. "$ROOT/tests/redteam/lib.sh"
E="${ENGINE:?path to the lockflight binary}"
W="$1"; rm -rf "${W:?}"; mkdir -p "$W/repo" "$W/temp"
W="$(cd "$W" && pwd)"
cp -R "$ROOT/samples/dotnet-single/." "$W/repo/"; rm -rf "$W"/repo/*/*/bin "$W"/repo/*/*/obj
rm -f "$W/repo/tests/Shop.Tests/ContainerSmokeTests.cs"
export LOCKFLIGHT_TEMP="$W/temp"
export GIT_AUTHOR_NAME=dev GIT_AUTHOR_EMAIL=dev@example.com GIT_COMMITTER_NAME=dev GIT_COMMITTER_EMAIL=dev@example.com
cd "$W/repo" || exit 1
git init -q -b main; git config commit.gpgsign false; git config core.autocrlf false
printf 'version: 2\ngates:\n  quality:\n    suppressions: report\n  docs:\n    references: report\napprovals:\n  check: report\n' > lockflight.yaml
printf 'bin/\nobj/\n.lockflight/ledger/\n.lockflight/local/\n' > .gitignore
mkdir -p specs/001-shipping
printf '# Feature Specification: Shipping\n\n## Requirements *(mandatory)*\n\n- **FR-001**: System MUST charge 5.00 for the first kilogram\n- **FR-002**: System MUST show the shipping cost on the receipt\n' > specs/001-shipping/spec.md
git add -A; git commit -qm base; main=$(git rev-parse HEAD)
calc=src/Shop.Api/PriceCalculator.cs; tests=tests/Shop.Tests/PriceCalculatorTests.cs; project=tests/Shop.Tests/Shop.Tests.csproj

# judge <head> <base> <branch>: the judge as the action runs it, into a fresh evidence folder; then the report's comment.
judge() {
  rm -rf "$W/evidence" "$W/verdict"
  "$E" ci --platform github --head "$1" --base "$2" --branch "$3" --no-review-expected --evidence "$W/evidence" > "$W/judge.log" 2>&1
  local status=$?
  "$E" ci report --evidence "$W/evidence" --head "$1" --out "$W/verdict" > /dev/null 2>&1
  return "$status"
}
# ran <exit code>: the judge wrote its evidence.
ran() {
  if [ -f "$W/evidence/evidence.json" ]; then ok "the judge ran (exit $1)"; else bad "the judge wrote no evidence (exit $1)"; sed 's/^/     /' "$W/judge.log"; fi
}
# gates_step <step id>: the step's verdict, then its detail, from the gates' result in the evidence.
gates_step() {
  "$PY" -c 'import json,sys
step = next((s for s in json.load(open(sys.argv[1]))["steps"] if s["id"] == sys.argv[2]), {})
print(step.get("verdict", "none")); print(json.dumps(step))' "$W/evidence/gates-result.json" "$1"
}

# ---------------------------------------------------------------------------------------------------------------------------
case_start "a package's build targets, and the test code, put module initializers that rewrite results into the test assembly"
git checkout -q -b test-kit "$main"
# The package (committed with the pull request, restored from a folder the project names) adds a C# file of its own to every
# project that references it: the code that runs is in no file of the diff.
mkdir -p tests/Shop.Tests/packages "$W/kit/build"
cat > "$W/kit/Shop.TestKit.nuspec" <<'NUSPEC'
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata><id>Shop.TestKit</id><version>1.0.0-redteam</version><authors>dev</authors><description>Test helpers</description></metadata>
</package>
NUSPEC
printf '<Project>\n  <ItemGroup>\n    <Compile Include="$(MSBuildThisFileDirectory)PackageForge.cs" />\n  </ItemGroup>\n</Project>\n' > "$W/kit/build/Shop.TestKit.targets"
cat > "$W/kit/build/PackageForge.cs" <<'FORGE'
#pragma warning disable CA2255
namespace Shop.TestKit;

internal static class PackageForge
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Install() =>
        System.AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { System.IO.File.WriteAllText(System.IO.Path.Combine("TestResults", "forged.trx"), "<TestRun />"); }
            catch (System.IO.IOException) { }
        };
}
FORGE
"$PY" -I -c 'import os, sys, zipfile
kit, out = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(out, "w") as package:
    for name in ("Shop.TestKit.nuspec", "build/Shop.TestKit.targets", "build/PackageForge.cs"):
        package.write(os.path.join(kit, name), name)' "$W/kit" tests/Shop.Tests/packages/shop.testkit.1.0.0-redteam.nupkg
rm -rf "${NUGET_PACKAGES:-$HOME/.nuget/packages}/shop.testkit"
perl -0pi -e 's#</Project>#  <PropertyGroup>\n    <RestoreAdditionalProjectSources>\$(MSBuildThisFileDirectory)packages</RestoreAdditionalProjectSources>\n  </PropertyGroup>\n  <ItemGroup>\n    <PackageReference Include="Shop.TestKit" Version="1.0.0-redteam" />\n  </ItemGroup>\n</Project>#' "$project"
cat > tests/Shop.Tests/ResultForge.cs <<'FORGE'
#pragma warning disable CA2255
namespace Shop.Tests;

internal static class ResultForge
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Install() =>
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { File.WriteAllText(Path.Combine("TestResults", "passed.trx"), "<TestRun />"); }
            catch (IOException) { }
        };
}
FORGE
git add -A; git commit -qm "test: shared test kit"
judge "$(git rev-parse HEAD)" "$main" test-kit; ran $?
check "$W/evidence/evidence.json" tests.hostTampering NOT-EARNED
check_reason "$W/evidence/evidence.json" tests.hostTampering HOST_TAMPERING
contains "$W/evidence/evidence.json" "PackageForge" "the package's module initializer is found in the assembly that runs"
contains "$W/evidence/evidence.json" "ResultForge" "the test code's module initializer is found"
guide_says ci-hardening.md "the host-tampering check lists the plain forms of it and is NOT-EARNED when one is new"

# ---------------------------------------------------------------------------------------------------------------------------
# A base with a strict policy and the generated workflow's action pin, for the pull request that loosens them.
git checkout -q -b strict "$main"
cat > lockflight.yaml <<'YAML'
version: 2
protected: ["src/Shop.Api/Pricing/**"]
stop:
  gate: true
gates:
  timeoutSeconds: 600
  quality:
    suppressions: fail
  docs:
    references: fail
approvals:
  check: fail
routing:
  preset: strict
  fix: { maxLines: 10, maxFiles: 1 }
  critical: ["src/Shop.Api/Payments/**"]
  require:
    reviewFrom: behaviour
ci:
  timeoutMinutes: 45
  flakyRetries: 1
  maxRedCommits: 5
  redCommits: true
YAML
pin_base=$(printf '1%.0s' $(seq 40)); pin_other=$(printf '2%.0s' $(seq 40))
mkdir -p .github/workflows
printf 'name: lockflight\non: pull_request\njobs:\n  judge:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: lockflight/lockflight/action@%s # v0.3.1\n' "$pin_base" > .github/workflows/lockflight-gates.yml
git add -A; git commit -qm "chore: strict policy"; strict=$(git rev-parse HEAD)

case_start "git hooks, a filter and git settings committed in the pull request; build control and the action pin loosened; every policy key loosened"
git checkout -q -b loosen "$strict"
ran_marker="$W/committed-git-code-ran"
mkdir -p .githooks
for hook in post-checkout post-merge pre-commit reference-transaction post-index-change; do
  printf '#!/bin/sh\ntouch "%s"\n' "$ran_marker" > ".githooks/$hook"; chmod +x ".githooks/$hook"
done
printf '* filter=lfpwn\n' > .gitattributes
printf '[core]\n\thooksPath = .githooks\n[filter "lfpwn"]\n\tsmudge = sh -c '"'"'touch %s; cat'"'"'\n\tclean = cat\n\trequired = true\n' "$ran_marker" > .gitconfig
perl -0pi -e 's#<TreatWarningsAsErrors>true</TreatWarningsAsErrors>#<TreatWarningsAsErrors>false</TreatWarningsAsErrors>\n    <RunAnalyzers>false</RunAnalyzers>#' Directory.Build.props
perl -pi -e "s/$pin_base/$pin_other/" .github/workflows/lockflight-gates.yml
cat > lockflight.yaml <<'YAML'
version: 2
protected: []
stop:
  gate: false
gates:
  timeoutSeconds: 7200
  quality:
    suppressions: report
  docs:
    references: report
approvals:
  check: report
routing:
  preset: strict
  fix: { maxLines: 100, maxFiles: 1 }
  critical: []
  require:
    reviewFrom: critical
    reviewKeyFrom: critical
ci:
  timeoutMinutes: 360
  flakyRetries: 5
  maxRedCommits: 50
  redCommits: false
  lowRiskPaths: ["src/**"]
YAML
git add -A; git commit -qm "chore: settings"
judge "$(git rev-parse HEAD)" "$strict" loosen; ran $?
if [ -e "$ran_marker" ]; then bad "a committed hook, filter or git setting ran"; else ok "no committed hook, filter or git setting ran"; fi
guide_says ci-hardening.md "Hooks, filters and settings committed in the pull request's tree are never run"
protected=$(gates_step policy.protected)
for file in Directory.Build.props .github/workflows/lockflight-gates.yml .gitattributes lockflight.yaml; do
  if grep -qF "$file" <<< "$protected"; then ok "policy.protected lists $file"; else bad "policy.protected does not list $file"; echo "$protected" | sed 's/^/     /'; fi
done
guide_says ci-hardening.md "\`policy.protected\` lists these files for review. **CODEOWNERS is what protects them.**"
guide_says ci-hardening.md "The judge does not check which commit the pin names"
check "$W/evidence/evidence.json" policy FAIL
loosened=$("$PY" -c 'import json,sys; print("\n".join(json.load(open(sys.argv[1]))["policy"].get("loosened", [])))' "$W/evidence/evidence.json")
for key in "protected:" "stop.gate:" "gates.quality.suppressions:" "gates.docs.references:" "approvals.check:" "routing.fix.maxLines:" \
  "routing.critical:" "routing.require.reviewFrom:" "routing.require.reviewKeyFrom:" "ci.flakyRetries:" "ci.maxRedCommits:" "ci.lowRiskPaths:"; do
  if grep -qF -- "$key" <<< "$loosened"; then ok "the policy check names $key"; else bad "the policy check does not name $key"; echo "$loosened" | sed 's/^/     /'; fi
done
for key in "gates.timeoutSeconds" "ci.timeoutMinutes" "ci.redCommits"; do
  if grep -qF -- "$key" <<< "$loosened"; then bad "the policy check now names $key: update the guide's list of keys it does not compare"
  else ok "$key is not compared, as the guide says"; fi
done
guide_says guardrails-and-boundaries.md "Time limits, the review model and budget, the build file, the stop filter and \`ci.redCommits\` are not compared"

# ---------------------------------------------------------------------------------------------------------------------------
case_start "a requirement the base had is marked [outside .NET], and a [structural] requirement gets a test that holds on both sides"
git checkout -q -b markers "$main"
perl -0pi -e 's/\}\s*\z/\n    public decimal ShippingCost(decimal weightKg) => throw new NotImplementedException();\n}\n/' "$calc"
perl -0pi -e 's/\}\s*\z/\n    [Fact]\n    [Trait("Requirement", "FR-001")]\n    public void TheFirstKilogramCostsFive() => Assert.Equal(5m, new PriceCalculator().ShippingCost(1m));\n}\n/' "$tests"
git add -A; git commit -qm "test(001-shipping): red for US1" -m "Lockflight-Red: 1"
perl -pi -e 's/=> throw new NotImplementedException\(\);/=> 5m;/' "$calc"
perl -pi -e 's/- \*\*FR-002\*\*:/- **FR-002** [outside .NET: README.md]:/' specs/001-shipping/spec.md
printf -- '- **FR-003** [structural]: Discounts MUST round to cents as before\n' >> specs/001-shipping/spec.md
printf '# Shop\n\nFR-002: the receipt shows the shipping cost.\n' > README.md
cat > tests/Shop.Tests/DiscountStructureTests.cs <<'TEST'
using Shop.Api;

namespace Shop.Tests;

public sealed class DiscountStructureTests
{
    [Fact]
    [Trait("Requirement", "FR-003")]
    public void TenPercentOffAHundredIsNinety() => Assert.Equal(90m, new PriceCalculator().ApplyDiscount(100m, 10m));
}
TEST
git add -A; git commit -qm "feat(001-shipping): US1"
judge "$(git rev-parse HEAD)" "$main" 001-shipping; ran $?
check "$W/evidence/evidence.json" trace PASS
contains "$W/verdict/comment.md" "1 requirement verified outside .NET" "the headline counts the requirement left out of trace"
contains "$W/verdict/comment.md" "Requirement markers changed on requirements the base already had" "the comment calls out the marker put on FR-002"
contains "$W/verdict/comment.md" "1 structural requirement" "the headline counts the structural requirement"
guide_says guardrails-and-boundaries.md "the marker is shown in the approval digest and the comment, and is for a person to accept"
guide_says ci-hardening.md "This shows the test's assertions held on both sides, not that they pin the requirement; that is for review."

# ---------------------------------------------------------------------------------------------------------------------------
case_start "an implementation that hangs at head after a fast red, to burn the judge's time"
git checkout -q -b short-limit "$main"
printf 'version: 2\ngates:\n  timeoutSeconds: 60\n  quality:\n    suppressions: report\n  docs:\n    references: report\napprovals:\n  check: report\nci:\n  flakyRetries: 0\n' > lockflight.yaml
git commit -qam "chore: a one-minute step limit"; short=$(git rev-parse HEAD)
git checkout -q -b hang "$short"
perl -0pi -e 's/\}\s*\z/\n    public decimal ShippingCost(decimal weightKg) => throw new NotImplementedException();\n}\n/' "$calc"
perl -0pi -e 's/\}\s*\z/\n    [Fact]\n    [Trait("Requirement", "FR-001")]\n    public void TheFirstKilogramCostsFive() => Assert.Equal(5m, new PriceCalculator().ShippingCost(1m));\n}\n/' "$tests"
git add -A; git commit -qm "test(001-shipping): red for US1" -m "Lockflight-Red: 1"
perl -pi -e 's/=> throw new NotImplementedException\(\);/{ Thread.Sleep(Timeout.Infinite); return 5m; }/' "$calc"
git commit -qam "feat(001-shipping): US1"
started=$(date +%s)
judge "$(git rev-parse HEAD)" "$short" 001-shipping; code=$?; ran "$code"
echo "     the judge took $(( $(date +%s) - started )) s"
check "$W/evidence/evidence.json" red PASS
check "$W/evidence/evidence.json" gates ERROR
step=$(gates_step test)
if grep -qF STEP_TIMED_OUT <<< "$step"; then ok "the gates' test step names STEP_TIMED_OUT"; else bad "the gates' test step does not time out"; echo "$step" | sed 's/^/     /'; fi
if [ "$code" = 0 ]; then bad "the judge passed a head that hangs"; else ok "the judge does not pass a head that hangs"; fi
guide_says commands.md "each build or test process is bounded by the time left before that reserve, so the evidence is always written"

# ---------------------------------------------------------------------------------------------------------------------------
case_start "known gaps: what the judge does not catch, and the guide entry that says so"
guide_says ci-hardening.md "a \`.runsettings\` data collector, or a test platform extension package, rewrites outcomes without code in the test assembly"

finish "Red-team judge"
