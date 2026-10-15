using Lockflight.Cli.Lifecycle;

namespace Lockflight.Cli.Tests.Lifecycle;

public sealed class TasksDocumentTests
{
    [Fact]
    public void TasksCarryStoryRequirementsAndTestRole()
    {
        const string tasks = """
            ## Phase 3: User Story 1 - Pay for shipping (Priority: P1)

            ### Tests for User Story 1 (MANDATORY — write first, ensure they FAIL) ⚠️

            - [ ] T010 [P] [US1] (FR-001, FR-002) Shipping cost tests in tests/Shop.Tests/ShippingTests.cs

            ### Implementation for User Story 1

            - [x] T011 [US1] (FR-001) Declare ShippingCost stub in src/Shop.Api/PriceCalculator.cs
            - [ ] T012 [US1] Implement ShippingCost
            - [ ] T013 Write tests for the edge cases (FR-003)
            """;

        IReadOnlyList<TaskLine> parsed = TasksDocument.Parse(tasks);

        Assert.Equal(["T010", "T011", "T012", "T013"], parsed.Select(task => task.Id));
        Assert.Equal(["FR-001", "FR-002"], parsed[0].Requirements);
        Assert.Equal(TaskRole.Test, parsed[0].Role);
        Assert.Equal(1, parsed[1].Story);
        Assert.True(parsed[1].IsDone);
        Assert.Equal(TaskRole.Stub, parsed[1].Role);
        Assert.Empty(parsed[2].Requirements);
        Assert.Equal(TaskRole.Implementation, parsed[2].Role);
        Assert.Equal(["FR-003"], parsed[3].Requirements);
        Assert.Null(parsed[3].Story);
        Assert.Equal(TaskRole.Test, parsed[3].Role);
    }

    [Theory]
    [InlineData("Spec for the cart total in web/src/cart/total.spec.ts")]
    [InlineData("Specs for checkout")]
    [InlineData("Playwright spec for the checkout flow")]
    [InlineData("Cart total in web/src/cart/total.spec.ts")]
    [InlineData("Cart badge in web/src/cart/Badge.test.tsx")]
    [InlineData("Price rounding in tests/Shop.Tests/RoundingTests.cs")]
    [InlineData("Price rounding in tests/Shop.UnitTests/RoundingTest.cs")]
    [InlineData("Price rounding cases in tests/Shop.Tests/")]
    [InlineData("Test the shipping calculator")]
    [InlineData("Vitest tests for the cart store")]
    [InlineData("Cover the shipping calculator with tests")]
    [InlineData("Cover rounding with unit tests in tests/Shop.Tests/RoundingTests.cs")]
    public void TestWordingAndTestFilesMarkATestTask(string description)
    {
        TaskLine task = Assert.Single(TasksDocument.Parse($"- [ ] T012 [US1] (FR-001) {description}"));

        Assert.Equal(TaskRole.Test, task.Role);
    }

    [Theory]
    [InlineData("Specify the shipping rules in docs/shipping.md")]
    [InlineData("Inspect the shipping calculator")]
    [InlineData("Update spec.md with the new limits")]
    [InlineData("Add the Contest entity in src/Shop.Api/Contest.cs")]
    [InlineData("Wire the testing page in web/src/pages/testing.ts")]
    [InlineData("Cover the edge cases in the calculator")]
    public void OtherWordingIsImplementation(string description)
    {
        TaskLine task = Assert.Single(TasksDocument.Parse($"- [ ] T014 [US1] (FR-001) {description}"));

        Assert.Equal(TaskRole.Implementation, task.Role);
        Assert.Contains("no test heading, no test verb, no test file", task.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnImplementationVerbWinsOverATestFileName()
    {
        TaskLine task = Assert.Single(TasksDocument.Parse("- [ ] T014 [US1] (FR-001) Implement the cart total so web/src/cart/total.spec.ts passes"));

        Assert.Equal(TaskRole.Implementation, task.Role);
        Assert.Contains("total.spec.ts does not make it a test", task.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Declare ShippingCost stub in src/Shop.Api/PriceCalculator.cs")]
    [InlineData("Add stubs for the cart store in web/src/cart/store.ts")]
    [InlineData("Skeleton of the CartService in web/src/cart/service.ts")]
    [InlineData("Scaffold signatures of PriceCalculator in src/Shop.Api/PriceCalculator.cs")]
    [InlineData("Declare the public signatures of PriceCalculator")]
    [InlineData("Add ShippingCost throwing NotImplementedException")]
    public void StubWordingMarksAStubTask(string description)
    {
        TaskLine task = Assert.Single(TasksDocument.Parse($"- [ ] T011 [US1] (FR-001) {description}"));

        Assert.Equal(TaskRole.Stub, task.Role);
    }

    [Theory]
    [InlineData("Declare ShippingCost in src/Shop.Api/PriceCalculator.cs")]
    [InlineData("Handle stubborn retries in the shipping client")]
    [InlineData("Read the signatures of incoming webhooks")]
    public void WordingNearStubMarkersIsNotAStub(string description)
    {
        TaskLine task = Assert.Single(TasksDocument.Parse($"- [ ] T011 [US1] (FR-001) {description}"));

        Assert.Equal(TaskRole.Implementation, task.Role);
    }

    [Fact]
    public void ATestsHeadingWinsOverStubWording()
    {
        const string tasks = """
            ### Tests for User Story 1
            - [ ] T012 [US1] (FR-001) Shipping tests against the ShippingCost stub
            """;

        TaskLine task = Assert.Single(TasksDocument.Parse(tasks));

        Assert.Equal(TaskRole.Test, task.Role);
        Assert.Contains("### Tests", task.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void RequirementsAnywhereInTheLineAreCitedOnce()
    {
        const string tasks = """
            - [ ] T012 [US1] Shipping cost in src/Shop.Api/PriceCalculator.cs (FR-003, FR-004)
            - [ ] T013 [US1] Rounding rule that covers FR-003
            - [ ] T014 [US1] (FR-001) Shipping for FR-001 and FR-002
            - [ ] T015 [US1] Pricing helpers in src/XFR-0010/Helpers.cs
            """;

        IReadOnlyList<TaskLine> parsed = TasksDocument.Parse(tasks);

        Assert.Equal(["FR-003", "FR-004"], parsed[0].Requirements);
        Assert.Equal(["FR-003"], parsed[1].Requirements);
        Assert.Equal(["FR-001", "FR-002"], parsed[2].Requirements);
        Assert.Empty(parsed[3].Requirements);
    }
}
