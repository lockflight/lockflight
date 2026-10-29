using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Testing;

public sealed class TestInventoryTests
{
    [Fact]
    public void TheJsonListingGivesKeysTraitsAndSkips()
    {
        const string output = """
            [{"Assembly":"/x/Shop.Tests.dll","DisplayName":"d","ID":"1","Class":"Shop.Tests.ProbeTests","Method":"Stub","Explicit":false,"Traits":{"Requirement":["FR-003"]}},{"Assembly":"/x/Shop.Tests.dll","Class":"Shop.Tests.ProbeTests+Nested","Method":"Inner","Explicit":false,"Skip":"later"}]
            [0m
            """;

        IReadOnlyList<TestCase> tests = TestInventory.Parse("Shop.Tests", output)!;

        Assert.Equal(["Shop.Tests:Shop.Tests.ProbeTests.Stub", "Shop.Tests:Shop.Tests.ProbeTests+Nested.Inner"], tests.Select(test => test.Key));
        Assert.Equal(["FR-003"], tests[0].Requirements);
        Assert.Equal([KeyValuePair.Create("Requirement", "FR-003")], tests[0].Traits);
        Assert.Empty(tests[1].Traits);
        Assert.Equal("later", tests[1].SkipReason);
        Assert.Equal("Shop.Tests.ProbeTests+Nested.Inner", tests[1].MethodFilter);
    }

    [Fact]
    public void MtpTextListingGivesKeysTraitsSkipsAndCollapsesTheoryRows()
    {
        const string output = """

            Assembly: Parlot.Tests
              - Display name: "Parlot.Tests.LockflightProbeTests.Probe"
                Test method:  Parlot.Tests.LockflightProbeTests.Probe
                ID:           f48c
                Traits:
                  "Category": ["Fast"]
                  "Requirement": ["FR-001", "FR-002"]
              - Display name: "Parlot.Tests.LockflightProbeTests.Skipped"
                Test method:  Parlot.Tests.LockflightProbeTests.Skipped
                ID:           4a71
                Skip reason:  "later"
              - Display name: "Parlot.Tests.NumberTests.Rows(source: \"0\")"
                Test method:  Parlot.Tests.NumberTests.Rows
                ID:           4a2e
              - Display name: "Parlot.Tests.NumberTests.Rows(source: \"1\")"
                Test method:  Parlot.Tests.NumberTests.Rows
                ID:           6a11
              - Display name: "Parlot.Tests.LockflightProbeTests+Nested.Inner"
                Test method:  Parlot.Tests.LockflightProbeTests+Nested.Inner
                ID:           5714
            """;

        IReadOnlyList<TestCase> tests = TestInventory.ParseText("Parlot.Tests", output);

        Assert.Equal(4, tests.Count);
        Assert.Equal(["FR-001", "FR-002"], tests[0].Requirements);
        Assert.Equal(
            [KeyValuePair.Create("Category", "Fast"), KeyValuePair.Create("Requirement", "FR-001"), KeyValuePair.Create("Requirement", "FR-002")],
            tests[0].Traits);
        Assert.Equal("later", tests[1].SkipReason);
        Assert.Equal("Parlot.Tests:Parlot.Tests.NumberTests.Rows", tests[2].Key);
        Assert.Equal("Parlot.Tests.LockflightProbeTests+Nested", tests[3].Class);
        Assert.DoesNotContain(tests, test => test.IsExplicit);
    }

    [Fact]
    public void TheJsonListingMarksExplicitTests()
    {
        const string output = """
            [{"Class":"Shop.Tests.XTests","Method":"Exp","Explicit":true,"Traits":{"Requirement":["FR-009"]}},{"Class":"Shop.Tests.XTests","Method":"Plain","Explicit":false},{"Class":"Shop.Tests.XTests","Method":"Older"}]
            """;

        IReadOnlyList<TestCase> tests = TestInventory.Parse("Shop.Tests", output)!;

        Assert.Equal([true, false, false], tests.Select(test => test.IsExplicit));
    }

    [Fact]
    public void MtpTextListingMarksExplicitTestsWithoutLosingTheirTraits()
    {
        const string output = """
              - Display name: "Shop.Tests.XTests.Exp"
                Test method:  Shop.Tests.XTests.Exp
                ID:           2f95
                Explicit:     true
                Traits:
                  "Requirement": ["FR-009"]
              - Display name: "Shop.Tests.XTests.Plain"
                Test method:  Shop.Tests.XTests.Plain
                ID:           e6fd
            """;

        IReadOnlyList<TestCase> tests = TestInventory.ParseText("Shop.Tests", output);

        Assert.Equal([true, false], tests.Select(test => test.IsExplicit));
        Assert.Equal(["FR-009"], tests[0].Requirements);
    }
}
