# shellcheck shell=bash
# shellcheck disable=SC2034,SC2154 # run.sh reads CASES and FEATURE and sets main, the base commit
# The catch benchmark's pull requests on samples/dotnet-mstest (MSTest 4 on Microsoft.Testing.Platform): the story lends a
# copy of a title. See dotnet-single.sh for the shape.

FEATURE=001-lending
CASES=(
  "control|control|control|tests committed first with stubs (Lockflight-Red), then the implementation"
  "impl-in-red|test after code|cheat|the tests and the implementation in one commit, labelled as the red commit"
  "tautology|deleted or weakened assertion|cheat|a buggy implementation, and a cycle test's assertion replaced with one that always holds"
  "cycle-test-deleted|deleted or weakened assertion|cheat|a buggy implementation, and the cycle test that catches it deleted after green"
  "existing-test-skipped|skipped test|cheat|a regression in existing code, and the existing test that catches it ignored"
)

catalog=src/Lending/Catalog.cs
lend=tests/Lending.Tests/LendingTests.cs

fixture_setup() {
  cp -R "$ROOT/samples/dotnet-mstest/." .
  rm -rf ./*/*/bin ./*/*/obj
}

spec() {
  mkdir -p "specs/$FEATURE"
  printf '# Feature Specification: Lending\n\n## Requirements *(mandatory)*\n\n- **FR-003**: System MUST lend an available copy and count it out\n- **FR-004**: System MUST refuse to lend a title with no copy left\n' > "specs/$FEATURE/spec.md"
}
catalog_with() {
  git show "$main:$catalog" > "$catalog"
  BODY="$1" perl -0pi -e 's/\}\s*\z/\n$ENV{BODY}}\n/' "$catalog"
}
stub() { catalog_with '    public bool Lend(string title) => throw new NotImplementedException();
'; }
# impl [honest|zero]: zero lends from a title with no copy left.
impl() {
  local cond='available == 0'
  [ "${1:-honest}" = zero ] && cond='available < 0'
  catalog_with "    public bool Lend(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        int available = Copies(title);
        if ($cond)
        {
            return false;
        }

        copies[title] = available - 1;
        return true;
    }
"
}
tests_with() {
  { printf 'namespace Lending.Tests;\n\n[TestClass]\npublic sealed class LendingTests\n{'
    printf '%s' "$1"
    printf '}\n'; } > "$lend"
}
T_FR3='
    [TestMethod]
    [TestProperty("Requirement", "FR-003")]
    public void AnAvailableCopyIsLent()
    {
        Catalog catalog = new();
        catalog.AddCopies("Dune", 1);

        Assert.IsTrue(catalog.Lend("Dune"));
        Assert.AreEqual(0, catalog.Copies("Dune"));
    }
'
T_FR4='
    [TestMethod]
    [DataRow(0)]
    [DataRow(2)]
    [TestProperty("Requirement", "FR-004")]
    public void ATitleWithNoCopyLeftIsNotLent(int copies)
    {
        Catalog catalog = new();
        catalog.AddCopies("Dune", copies);
        for (int lent = 0; lent < copies; lent++)
        {
            catalog.Lend("Dune");
        }

        Assert.IsFalse(catalog.Lend("Dune"));
    }
'
red_commit() { git add -A; git commit -qm "test($FEATURE): red for US1" -m "Lockflight-Red: 1"; }
commit() { git add -A; git commit -qm "$1"; }
honest_story() { spec; stub; tests_with "$T_FR3$T_FR4"; red_commit; impl "${1:-honest}"; commit "feat($FEATURE): lending"; }

case_control() { honest_story; }
case_impl-in-red() { spec; impl; tests_with "$T_FR3$T_FR4"; red_commit; }
case_tautology() {
  honest_story zero
  perl -0pi -e 's/Assert\.IsFalse\(catalog\.Lend\("Dune"\)\);/catalog.Lend("Dune");\n        Assert.IsNotNull(catalog);/' "$lend"
  commit "test($FEATURE): simplify"
}
case_cycle-test-deleted() { honest_story zero; tests_with "$T_FR3"; commit "test($FEATURE): remove a brittle test"; }
case_existing-test-skipped() {
  honest_story
  perl -pi -e 's/copies\[title\] = Copies\(title\) \+ count;/copies[title] = count;/' "$catalog"
  perl -0pi -e 's/(    \[TestMethod\]\n)(    \[TestProperty\("Requirement", "FR-001"\)\])/$1    [Ignore("flaky on CI")]\n$2/' tests/Lending.Tests/CatalogTests.cs
  commit "refactor: adding copies"
}
