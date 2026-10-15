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
        Assert.True(parsed[0].IsTest);
        Assert.Equal(1, parsed[1].Story);
        Assert.True(parsed[1].IsDone);
        Assert.False(parsed[1].IsTest);
        Assert.Empty(parsed[2].Requirements);
        Assert.Null(parsed[3].Story);
        Assert.True(parsed[3].IsTest);
    }
}
