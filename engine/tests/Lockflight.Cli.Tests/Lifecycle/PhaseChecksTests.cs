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

        Dictionary<string, PhaseCheckItem> items = PhaseChecks.Tasks(Feature, store.Check(ApprovalPhase.Spec), []).ToDictionary(item => item.Name);

        Assert.True(items["spec.approved"].Passed);
        Assert.Contains("FR-002", items["tasks.cover-requirements"].Detail, StringComparison.Ordinal);
        Assert.Contains("FR-009", items["tasks.cite-requirements"].Detail, StringComparison.Ordinal);
        Assert.Contains("US1", items["tasks.tests-first"].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AUnitTagMustNameAUnitOfTheWorkspace()
    {
        Directory.CreateDirectory(Feature.SpecDirectory);
        File.WriteAllText(Feature.SpecPath, Spec);
        File.WriteAllText(Feature.TasksPath, "- [ ] T010 [US1] (FR-001) [unit:services/orders] Order totals\n- [ ] T011 [US1] (FR-001) [unit:services/missing] Billing\n");

        Dictionary<string, PhaseCheckItem> items = PhaseChecks.Tasks(Feature, new ApprovalStore(Feature).Check(ApprovalPhase.Spec), ["services/orders"]).ToDictionary(item => item.Name);

        Assert.False(items["tasks.units"].Passed);
        Assert.Contains("services/missing", items["tasks.units"].Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("services/orders", items["tasks.units"].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnapprovedSpecFailsTheTasksPhase()
    {
        Directory.CreateDirectory(Feature.SpecDirectory);
        File.WriteAllText(Feature.SpecPath, Spec);

        PhaseCheckItem approval = PhaseChecks.Tasks(Feature, new ApprovalStore(Feature).Check(ApprovalPhase.Spec), [])[0];

        Assert.False(approval.Passed);
        Assert.Contains("lockflight approve spec", approval.Detail, StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
