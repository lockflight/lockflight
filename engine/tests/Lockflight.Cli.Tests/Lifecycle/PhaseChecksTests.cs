using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.Approvals;

namespace Lockflight.Cli.Tests.Lifecycle;

public sealed class PhaseChecksTests : IDisposable
{
    private const string Spec = """
        ### User Story 1 - Shipping (Priority: P1)

        1. **Given** a parcel, **When** priced, **Then** shipping is charged

        ## Requirements

        - **FR-001**: System MUST charge for the first kilogram
        - **FR-002**: System MUST charge per further started kilogram
        """;

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-phase-");

    private Feature Feature => new("f1", Path.Combine(root.FullName, "specs", "f1"), Path.Combine(root.FullName, ".lockflight", "features", "f1"));

    [Fact]
    public void ACompleteSpecPassesAndAnOpenClarificationFails()
    {
        Directory.CreateDirectory(Feature.SpecDirectory);
        File.WriteAllText(Feature.SpecPath, Spec);
        Assert.All(PhaseChecks.Specify(Feature), item => Assert.True(item.Passed, item.Detail));

        File.AppendAllText(Feature.SpecPath, "- **FR-003**: Free above [NEEDS CLARIFICATION: threshold]\n");
        Assert.Contains(PhaseChecks.Specify(Feature), item => item.Name == "spec.clarified" && !item.Passed);
    }

    [Fact]
    public void TemplateRequirementsAndStoriesFailTheSpecifyPhase()
    {
        Directory.CreateDirectory(Feature.SpecDirectory);
        File.WriteAllText(Feature.SpecPath, """
            ### User Story 1 - [Brief Title] (Priority: P1)

            1. **Given** [initial state], **When** [action], **Then** [expected outcome]

            - **FR-001**: System MUST [specific capability, e.g., "allow users to create accounts"]
            """);

        PhaseCheckItem placeholders = Assert.Single(PhaseChecks.Specify(Feature), item => item.Name == "spec.no-placeholders");

        Assert.False(placeholders.Passed);
        Assert.Contains("line 1: [Brief Title]", placeholders.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void TasksMustCoverRequirementsAndPutTestsFirst()
    {
        Directory.CreateDirectory(Feature.SpecDirectory);
        File.WriteAllText(Feature.SpecPath, Spec);
        File.WriteAllText(Feature.TasksPath, """
            - [ ] T001 Create the solution folder
            ### Implementation for User Story 1
            - [ ] T010 [US1] (FR-001) Declare ShippingCost stub in src/Shop.Api/PriceCalculator.cs
            - [ ] T011 [US1] (FR-001) Implement ShippingCost
            ### Tests for User Story 1
            - [ ] T012 [US1] (FR-001, FR-009) Shipping tests in tests/Shop.Tests/ShippingTests.cs
            """);
        ApprovalStore store = new(Feature);
        store.Save(ApprovalPhase.Spec, "dev@example.com", DateTimeOffset.UnixEpoch, "0.0.0-test");

        Dictionary<string, PhaseCheckItem> items = PhaseChecks.Tasks(Feature, store.Check(ApprovalPhase.Spec), [], root.FullName).ToDictionary(item => item.Name);

        Assert.True(items["spec.approved"].Passed);
        Assert.Contains("FR-002", items["tasks.cover-requirements"].Detail, StringComparison.Ordinal);
        Assert.Contains("FR-009", items["tasks.cite-requirements"].Detail, StringComparison.Ordinal);
        Assert.Contains("US1", items["tasks.tests-first"].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AMisorderedStoryNamesEachTaskItsLineAndWhy()
    {
        Directory.CreateDirectory(Feature.SpecDirectory);
        File.WriteAllText(Feature.SpecPath, Spec);
        File.WriteAllText(Feature.TasksPath, """
            ### Implementation for User Story 1
            - [ ] T010 [US1] (FR-001) Skeleton of PriceCalculator in src/Shop.Api/PriceCalculator.cs
            - [ ] T011 [US1] (FR-001) Shipping cost in src/Shop.Api/PriceCalculator.cs
            - [ ] T012 [US1] (FR-002) Spec for the shipping badge in web/src/Badge.spec.ts
            - [ ] T013 [US1] (FR-002) Badge component in web/src/Badge.tsx
            - [ ] T020 [US2] (FR-002) Weight limits in src/Shop.Api/Limits.cs
            """);

        PhaseCheckItem item = PhaseChecks.Tasks(Feature, new ApprovalStore(Feature).Check(ApprovalPhase.Spec), [], root.FullName).Single(check => check.Name == "tasks.tests-first");

        Assert.False(item.Passed);
        Assert.Contains("T011 line 3: implementation (no test heading, no test verb, no test file", item.Detail, StringComparison.Ordinal);
        Assert.Contains("T012 (line 4) of US1", item.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("T010", item.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("T013", item.Detail, StringComparison.Ordinal);
        Assert.Contains("US2 has no test task", item.Detail, StringComparison.Ordinal);
        Assert.Contains("### Tests for", item.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendSpecsAndRequirementsAtTheEndOfTheLinePassTheTasksPhase()
    {
        Directory.CreateDirectory(Feature.SpecDirectory);
        File.WriteAllText(Feature.SpecPath, Spec);
        File.WriteAllText(Feature.TasksPath, """
            - [ ] T010 [US1] Declare the public signatures of the cart store in web/src/cart/store.ts (FR-001)
            - [ ] T011 [US1] Spec for the cart total in web/src/cart/total.spec.ts (FR-001, FR-002)
            - [ ] T012 [US1] Cart total in web/src/cart/total.ts, covers FR-001 and FR-002
            """);

        Dictionary<string, PhaseCheckItem> items = PhaseChecks.Tasks(Feature, new ApprovalStore(Feature).Check(ApprovalPhase.Spec), [], root.FullName).ToDictionary(item => item.Name);

        Assert.True(items["tasks.cover-requirements"].Passed, items["tasks.cover-requirements"].Detail);
        Assert.True(items["tasks.cite-requirements"].Passed, items["tasks.cite-requirements"].Detail);
        Assert.True(items["tasks.tests-first"].Passed, items["tasks.tests-first"].Detail);
    }

    [Fact]
    public void AUnitTagMustNameAUnitOfTheWorkspace()
    {
        Directory.CreateDirectory(Feature.SpecDirectory);
        File.WriteAllText(Feature.SpecPath, Spec);
        File.WriteAllText(Feature.TasksPath, "- [ ] T010 [US1] (FR-001) [unit:services/orders] Order totals\n- [ ] T011 [US1] (FR-001) [unit:services/missing] Billing\n");

        Dictionary<string, PhaseCheckItem> items = PhaseChecks.Tasks(Feature, new ApprovalStore(Feature).Check(ApprovalPhase.Spec), ["services/orders"], root.FullName).ToDictionary(item => item.Name);

        Assert.False(items["tasks.units"].Passed);
        Assert.Contains("services/missing", items["tasks.units"].Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("services/orders", items["tasks.units"].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnapprovedSpecFailsTheTasksPhase()
    {
        Directory.CreateDirectory(Feature.SpecDirectory);
        File.WriteAllText(Feature.SpecPath, Spec);

        PhaseCheckItem approval = PhaseChecks.Tasks(Feature, new ApprovalStore(Feature).Check(ApprovalPhase.Spec), [], root.FullName)[0];

        Assert.False(approval.Passed);
        Assert.Contains("lockflight approve spec", approval.Detail, StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
