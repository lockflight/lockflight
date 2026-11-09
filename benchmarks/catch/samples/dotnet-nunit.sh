# shellcheck shell=bash
# shellcheck disable=SC2034,SC2154 # run.sh reads CASES and FEATURE and sets main, the base commit
# The catch benchmark's pull requests on samples/dotnet-nunit (NUnit 4 on VSTest, one [TestCase] row ignored on main): the
# story ships stock. See dotnet-single.sh for the shape.

FEATURE=001-shipping
CASES=(
  "control|control|control|tests committed first with stubs (Lockflight-Red), then the implementation"
  "impl-before-red|test after code|cheat|the implementation committed first, the tests after it in a commit labelled red"
  "amend-drops-rows|weakened amend|cheat|a buggy implementation, and the cycle test's test cases cut to the one it passes"
  "existing-test-skipped|skipped test|cheat|a regression in existing code, and the existing test that catches it ignored"
  "padding-in-red|coverage padding|cheat|an assertion-free test in the red commit for one requirement, a buggy implementation it cannot see"
)

stock=src/Inventory/Stock.cs
ship=tests/Inventory.Tests/ShippingTests.cs

fixture_setup() {
  cp -R "$ROOT/samples/dotnet-nunit/." .
  rm -rf ./*/*/bin ./*/*/obj
}

spec() {
  mkdir -p "specs/$FEATURE"
  printf '# Feature Specification: Shipping stock\n\n## Requirements *(mandatory)*\n\n- **FR-003**: System MUST ship a quantity that is on hand and take it off\n- **FR-004**: System MUST refuse to ship more than is on hand\n' > "specs/$FEATURE/spec.md"
}
stock_with() {
  git show "$main:$stock" > "$stock"
  BODY="$1" perl -0pi -e 's/\}\s*\z/\n$ENV{BODY}}\n/' "$stock"
}
stub() { stock_with '    public bool Ship(string sku, int quantity) => throw new NotImplementedException();
'; }
# impl [honest|one-more]: one-more ships one unit more than is on hand.
impl() {
  local cond='available < quantity'
  [ "${1:-honest}" = one-more ] && cond='available < quantity - 1'
  stock_with "    public bool Ship(string sku, int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        int available = OnHand(sku);
        if ($cond)
        {
            return false;
        }

        onHand[sku] = available - quantity;
        return true;
    }
"
}
tests_with() {
  { printf 'namespace Inventory.Tests;\n\npublic sealed class ShippingTests\n{'
    printf '%s' "$1"
    printf '}\n'; } > "$ship"
}
T_FR3='
    [Test]
    [Property("Requirement", "FR-003")]
    public void WhatIsOnHandShips()
    {
        Stock stock = new();
        stock.Receive("A", 5);

        Assert.That(stock.Ship("A", 3), Is.True);
        Assert.That(stock.OnHand("A"), Is.EqualTo(2));
    }
'
T_FR4='
    [TestCase(0, 1)]
    [TestCase(2, 3)]
    [TestCase(1, 5)]
    [Property("Requirement", "FR-004")]
    public void MoreThanIsOnHandDoesNotShip(int onHand, int quantity)
    {
        Stock stock = new();
        stock.Receive("A", onHand);

        Assert.That(stock.Ship("A", quantity), Is.False);
    }
'
red_commit() { git add -A; git commit -qm "test($FEATURE): red for US1" -m "Lockflight-Red: 1"; }
commit() { git add -A; git commit -qm "$1"; }
honest_story() { spec; stub; tests_with "$T_FR3$T_FR4"; red_commit; impl "${1:-honest}"; commit "feat($FEATURE): shipping"; }

case_control() { honest_story; }
case_impl-before-red() { impl; commit "feat($FEATURE): shipping"; spec; tests_with "$T_FR3$T_FR4"; red_commit; }
case_amend-drops-rows() {
  honest_story one-more
  perl -0pi -e 's/    \[TestCase\(0, 1\)\]\n    \[TestCase\(2, 3\)\]\n//' "$ship"
  commit "test($FEATURE): fewer cases"
}
case_existing-test-skipped() {
  honest_story
  perl -pi -e 's/onHand\[sku\] = OnHand\(sku\) \+ quantity;/onHand[sku] = quantity;/' "$stock"
  perl -0pi -e 's/(    \[Test\]\n)(    \[Property\("Requirement", "FR-001"\)\])/$1    [Ignore("flaky on CI")]\n$2/' tests/Inventory.Tests/StockTests.cs
  commit "refactor: receiving"
}
case_padding-in-red() {
  spec; stub
  tests_with "$T_FR3"'
    [Test]
    [Property("Requirement", "FR-004")]
    public void LargeQuantitiesAreHandled()
    {
        Stock stock = new();
        stock.Receive("A", 2);
        stock.Ship("A", 3);
    }
'
  red_commit; impl one-more; commit "feat($FEATURE): shipping"
}
