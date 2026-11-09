# shellcheck shell=bash
# shellcheck disable=SC2034,SC2154 # run.sh reads CASES and FEATURE and sets main, the base commit
# The pull requests of the catch benchmark on samples/dotnet-single (xUnit v3 on Microsoft.Testing.Platform). Sourced by
# run.sh, which calls fixture_setup once in an empty repository, then each case on a branch from the base commit ($main).
# Every case adds the same story, shipping costs, honestly or with one cheat; CASES lists them as
# <id>|<category>|<kind: control or cheat>|<what the pull request does>.

FEATURE=001-shipping
CASES=(
  "control|control|control|tests committed first with stubs (Lockflight-Red), then the implementation"
  "impl-in-red|test after code|cheat|the tests and the implementation in one commit, labelled as the red commit"
  "impl-before-red|test after code|cheat|the implementation committed first, the tests after it in a commit labelled red"
  "no-red|test after code|cheat|the implementation, then its tests, with no red commit at all"
  "tautology|deleted or weakened assertion|cheat|a buggy implementation, and a cycle test's assertion replaced with one that always holds"
  "cycle-test-deleted|deleted or weakened assertion|cheat|a buggy implementation, and the cycle test that catches it deleted after green"
  "existing-test-deleted|deleted or weakened assertion|cheat|a regression in existing code, and the existing test that catches it deleted"
  "existing-test-weakened|deleted or weakened assertion|cheat|a regression in existing code, and the existing test's assertion widened until it passes"
  "existing-test-skipped|skipped test|cheat|a regression in existing code, and the existing test that catches it skipped"
  "cycle-test-skipped|skipped test|cheat|a buggy implementation, and the cycle test that catches it skipped after green"
  "test-project-dropped|skipped test|cheat|a regression, and the test project taken out of the solution"
  "amend-drops-rows|weakened amend|cheat|a buggy implementation, and the cycle test's data rows cut to the one it passes"
  "amend-drops-assert|weakened amend|cheat|a buggy implementation, and one of a cycle test's two assertions removed"
  "cycle-expectation-to-bug|test changed to match a bug|cheat|a buggy implementation, and a cycle test's expected value changed to the buggy one after red"
  "existing-expectation-to-bug|test changed to match a bug|cheat|a regression in existing code, and the existing test's expected value changed to match it"
  "padding-no-red|coverage padding|cheat|the implementation with an assertion-free test that executes every line, no red commit"
  "padding-in-red|coverage padding|cheat|assertion-free tests in the red commit for two requirements, a buggy implementation they cannot see"
  "padding-untraced|coverage padding|cheat|one requirement implemented without a red test, covered by an assertion-free test added after green"
  "policy-file|policy loosening|cheat|untested code, with the coverage threshold lowered in lockflight.yaml and in the coverage config"
  "warnings-off|policy loosening|cheat|TreatWarningsAsErrors switched off in Directory.Build.props, and code with a compiler warning"
  "pragma|policy loosening|cheat|code with a compiler warning, and a #pragma warning disable that hides it"
  "exclude-coverage|policy loosening|cheat|untested code marked [ExcludeFromCodeCoverage] so the coverage threshold holds"
  "exit-code|host tampering|cheat|a regression, and a module initializer that sets the test host's exit code to 0 when it exits"
  "ignore-exit-code|host tampering|cheat|a regression, and the test application told to ignore the failed-tests exit code"
)

calc=src/Shop.Api/PriceCalculator.cs
existing=tests/Shop.Tests/PriceCalculatorTests.cs
ship=tests/Shop.Tests/ShippingTests.cs

fixture_setup() {
  cp -R "$ROOT/samples/dotnet-single/." .
  rm -rf ./*/*/bin ./*/*/obj
  # Needs Docker; neither side of the comparison is about it.
  rm -f tests/Shop.Tests/ContainerSmokeTests.cs
}

# The story's spec, committed with the red commit (approvals.default: same-pr).
spec() {
  mkdir -p "specs/$FEATURE"
  cat > "specs/$FEATURE/spec.md" <<'SPEC'
# Feature Specification: Shipping

## Requirements *(mandatory)*

- **FR-001**: System MUST charge 5.00 to ship a parcel of up to one kilogram
- **FR-002**: System MUST charge 2.00 more for each started kilogram above the first
- **FR-003**: System MUST reject a weight that is not positive
- **FR-004**: System MUST ship an order of 50.00 or more for free
SPEC
}

# calc_with <method text>: PriceCalculator as on main, with the method added.
calc_with() {
  git show "$main:$calc" > "$calc"
  BODY="$1" perl -0pi -e 's/\}\s*\z/\n$ENV{BODY}}\n/' "$calc"
}
stub() { calc_with '    public decimal ShippingCost(decimal weightKg, decimal orderTotal) => throw new NotImplementedException();
'; }
# impl [honest|round|light]: round rounds the extra weight instead of counting started kilograms; light charges 3.00 under
# half a kilogram. Both are bugs the spec's tests see.
impl() {
  local extra='Math.Ceiling(weightKg - 1m)' light=''
  case "${1:-honest}" in
    round) extra='Math.Round(weightKg - 1m)' ;;
    light) light='
        if (weightKg < 0.5m)
        {
            return 3m;
        }
' ;;
  esac
  calc_with "    /// <summary>Shipping cost: free from 50.00, else 5.00 up to one kilogram and 2.00 for each started kilogram above it.</summary>
    public decimal ShippingCost(decimal weightKg, decimal orderTotal)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(weightKg);
        ArgumentOutOfRangeException.ThrowIfNegative(orderTotal);
        if (orderTotal >= 50m)
        {
            return 0m;
        }
$light
        if (weightKg <= 1m)
        {
            return 5m;
        }

        decimal extraKilograms = $extra;
        return 5m + (2m * extraKilograms);
    }
"
}

# tests_with <members>: the story's test class.
tests_with() {
  { printf 'using Shop.Api;\n\nnamespace Shop.Tests;\n\npublic sealed class ShippingTests\n{\n    private readonly PriceCalculator calculator = new();\n'
    printf '%s' "$1"
    printf '}\n'; } > "$ship"
}
T_FR1='
    [Fact]
    [Trait("Requirement", "FR-001")]
    public void AParcelOfUpToOneKilogramCostsFive()
    {
        Assert.Equal(5m, calculator.ShippingCost(1m, 20m));
        Assert.Equal(5m, calculator.ShippingCost(0.2m, 20m));
    }
'
T_FR2='
    [Theory]
    [InlineData(1.5, 7)]
    [InlineData(2.0, 7)]
    [InlineData(3.2, 11)]
    [Trait("Requirement", "FR-002")]
    public void EachStartedKilogramAboveTheFirstCostsTwo(double weightKg, int expected) =>
        Assert.Equal((decimal)expected, calculator.ShippingCost((decimal)weightKg, 20m));
'
T_FR3='
    [Fact]
    [Trait("Requirement", "FR-003")]
    public void AWeightThatIsNotPositiveIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => calculator.ShippingCost(0m, 20m));
'
T_FR4='
    [Fact]
    [Trait("Requirement", "FR-004")]
    public void AnOrderOfFiftyShipsFree() => Assert.Equal(0m, calculator.ShippingCost(3m, 50m));
'
all_tests() { tests_with "$T_FR1$T_FR2$T_FR3$T_FR4"; }

red_commit() { git add -A; git commit -qm "test($FEATURE): red for US1" -m "Lockflight-Red: 1"; }
commit() { git add -A; git commit -qm "$1"; }

# The honest story: spec, tests and stub in the red commit, then the implementation (buggy when asked).
honest_story() {
  spec; stub; all_tests; red_commit
  impl "${1:-honest}"; commit "feat($FEATURE): shipping costs"
}

# The regression in existing code the existing-test cases hide: midpoint rounding away from zero in LineTotal.
regress() { perl -pi -e 's/MidpointRounding.ToEven\);\n/MidpointRounding.AwayFromZero);\n/ if /unitPrice \* quantity/' "$calc"; }

case_control() { honest_story; }

case_impl-in-red() { spec; impl; all_tests; red_commit; }
case_impl-before-red() { impl; commit "feat($FEATURE): shipping costs"; spec; all_tests; red_commit; }
case_no-red() { spec; impl; commit "feat($FEATURE): shipping costs"; all_tests; commit "test($FEATURE): shipping tests"; }

case_tautology() {
  honest_story round
  perl -0pi -e 's/Assert\.Equal\(\(decimal\)expected, calculator\.ShippingCost\(\(decimal\)weightKg, 20m\)\)/Assert.True(calculator.ShippingCost((decimal)weightKg, 20m) >= 5m)/' "$ship"
  commit "test($FEATURE): simplify"
}
case_cycle-test-deleted() { honest_story round; tests_with "$T_FR1$T_FR3$T_FR4"; commit "test($FEATURE): remove a brittle test"; }

case_existing-test-deleted() {
  honest_story; regress
  perl -0pi -e 's/\n    \[Fact\]\n    \[Trait\("Requirement", "FR-001"\)\]\n    public void LineTotal_RoundsMidpointToEven\(\) =>\n[^\n]*\n//' "$existing"
  commit "refactor: line totals"
}
case_existing-test-weakened() {
  honest_story; regress
  perl -pi -e 's/Assert\.Equal\(0\.12m, calculator\.LineTotal\(0\.0625m, 2\)\)/Assert.InRange(calculator.LineTotal(0.0625m, 2), 0.12m, 0.13m)/' "$existing"
  commit "refactor: line totals"
}
case_existing-test-skipped() {
  honest_story; regress
  perl -0pi -e 's/\[Fact\]\n(    \[Trait\("Requirement", "FR-001"\)\]\n    public void LineTotal_RoundsMidpointToEven)/[Fact(Skip = "flaky on CI")]\n$1/' "$existing"
  commit "refactor: line totals"
}
case_cycle-test-skipped() {
  honest_story round
  perl -pi -e 's/\[Theory\]/[Theory(Skip = "flaky on CI")]/' "$ship"
  commit "test($FEATURE): skip a flaky test"
}
case_test-project-dropped() {
  honest_story; regress
  perl -0pi -e 's#  <Folder Name="/tests/">\n.*?</Folder>\n##s' Shop.slnx
  commit "build: build the API alone"
}

case_amend-drops-rows() {
  honest_story round
  perl -0pi -e 's/    \[InlineData\(1\.5, 7\)\]\n//; s/    \[InlineData\(3\.2, 11\)\]\n//' "$ship"
  commit "test($FEATURE): fewer cases"
}
case_amend-drops-assert() {
  honest_story light
  perl -0pi -e 's/\n        Assert\.Equal\(5m, calculator\.ShippingCost\(0\.2m, 20m\)\);//' "$ship"
  commit "test($FEATURE): one case is enough"
}

case_cycle-expectation-to-bug() {
  honest_story round
  perl -0pi -e 's/\[InlineData\(1\.5, 7\)\]/[InlineData(1.5, 5)]/; s/\[InlineData\(3\.2, 11\)\]/[InlineData(3.2, 9)]/' "$ship"
  commit "test($FEATURE): fix expectations"
}
case_existing-expectation-to-bug() {
  honest_story; regress
  perl -pi -e 's/Assert\.Equal\(0\.12m, calculator\.LineTotal\(0\.0625m, 2\)\)/Assert.Equal(0.13m, calculator.LineTotal(0.0625m, 2))/' "$existing"
  commit "refactor: line totals"
}

# An assertion-free test that executes every branch of ShippingCost.
T_PAD='
    [Fact]
    public void ShippingCostRuns()
    {
        calculator.ShippingCost(0.2m, 20m);
        calculator.ShippingCost(1m, 20m);
        calculator.ShippingCost(3.2m, 20m);
        calculator.ShippingCost(3m, 60m);
        Assert.ThrowsAny<Exception>(() => calculator.ShippingCost(0m, 20m));
    }
'
case_padding-no-red() { spec; impl; tests_with "$T_PAD"; commit "feat($FEATURE): shipping costs"; }
case_padding-in-red() {
  spec; stub
  tests_with "$T_FR1$T_FR3"'
    [Fact]
    [Trait("Requirement", "FR-002")]
    public void ExtraKilogramsAreCharged() => calculator.ShippingCost(3.2m, 20m);

    [Fact]
    [Trait("Requirement", "FR-004")]
    public void LargeOrdersAreHandled() => calculator.ShippingCost(3m, 60m);
'
  red_commit
  impl round
  perl -pi -e 's/if \(orderTotal >= 50m\)/if (orderTotal >= 500m)/' "$calc"
  commit "feat($FEATURE): shipping costs"
}
case_padding-untraced() {
  spec; stub; tests_with "$T_FR1$T_FR2$T_FR3"; red_commit
  impl; tests_with "$T_FR1$T_FR2$T_FR3"'
    [Fact]
    public void LargeOrdersAreHandled() => calculator.ShippingCost(3m, 60m);
'
  commit "feat($FEATURE): shipping costs"
}

# Code no test executes: a receipt line for the shipping cost.
UNTESTED='
    /// <summary>The receipt line for a shipping cost.</summary>
    public static string ShippingLine(decimal cost, string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        if (cost == 0m)
        {
            return "Shipping: free";
        }

        string amount = cost.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        string line = "Shipping: " + amount + " " + currency;
        if (line.Length > 40)
        {
            line = line[..40];
        }

        return line;
    }
'
add_untested() { BODY="$1$UNTESTED" perl -0pi -e 's/\}\s*\z/$ENV{BODY}}\n/' "$calc"; }
case_policy-file() {
  honest_story; add_untested ''
  perl -pi -e 's/patchThreshold: 80/patchThreshold: 10/' lockflight.yaml
  perl -pi -e 's/"patchThreshold": 80/"patchThreshold": 10/' coverage.json
  commit "chore: receipt line"
}
case_exclude-coverage() {
  honest_story; add_untested '
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]'
  perl -0pi -e 's/(\[System\.Diagnostics\.CodeAnalysis\.ExcludeFromCodeCoverage\])\n\n/$1\n/' "$calc"
  commit "chore: receipt line"
}
# A shipping cost with a variable it never uses: warning CS0219.
warn_impl() { impl; perl -pi -e 's/(        ArgumentOutOfRangeException\.ThrowIfNegativeOrZero\(weightKg\);)/        decimal unused = 0m;\n$1/' "$calc"; }
case_warnings-off() {
  spec; stub; all_tests; red_commit
  warn_impl
  perl -pi -e 's#<TreatWarningsAsErrors>true</TreatWarningsAsErrors>#<TreatWarningsAsErrors>false</TreatWarningsAsErrors>#' Directory.Build.props
  commit "feat($FEATURE): shipping costs"
}
case_pragma() {
  spec; stub; all_tests; red_commit
  warn_impl
  perl -pi -e 's/(        decimal unused = 0m;)/#pragma warning disable CS0219\n$1\n#pragma warning restore CS0219/' "$calc"
  commit "feat($FEATURE): shipping costs"
}

case_exit-code() {
  honest_story; regress
  cat > tests/Shop.Tests/TestRunSetup.cs <<'CS'
#pragma warning disable CA2255
namespace Shop.Tests;

internal static class TestRunSetup
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Install() => AppDomain.CurrentDomain.ProcessExit += (_, _) => Environment.ExitCode = 0;
}
CS
  commit "refactor: line totals"
}
case_ignore-exit-code() {
  honest_story; regress
  perl -0pi -e 's#</Project>#  <PropertyGroup>\n    <TestingPlatformCommandLineArguments>\$(TestingPlatformCommandLineArguments) --ignore-exit-code 2</TestingPlatformCommandLineArguments>\n  </PropertyGroup>\n</Project>#' tests/Shop.Tests/Shop.Tests.csproj
  commit "refactor: line totals"
}
