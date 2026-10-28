namespace Lockflight.Cli.Tests.Lifecycle.Approvals;

/// <summary>A spec and tasks that pass their phase checks, as an approval needs.</summary>
internal static class ApprovableArtifacts
{
    public const string Spec = "# Orders\n\n### User Story 1 - Place an order (Priority: P1)\n\n"
        + "1. **Given** a full cart, **When** the customer checks out, **Then** an order is placed\n\n"
        + "- **FR-001**: Customers place orders\n- **FR-002**: Totals are shown in euros\n";

    public const string Tasks = "### Tests for User Story 1\n- [ ] T001 [US1] (FR-001) Test placing an order\n\n"
        + "### Implementation\n- [ ] T002 [US1] (FR-001, FR-002) Place the order\n";
}
