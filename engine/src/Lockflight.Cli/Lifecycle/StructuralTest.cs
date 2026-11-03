namespace Lockflight.Cli.Lifecycle;

/// <summary>
/// A test that names only structural requirements, with what it did on the code before the change and at head, and why
/// (<see cref="StructuralCheck"/>).
/// </summary>
internal sealed record StructuralTest(string Key, string MethodFilter, IReadOnlyList<string> Requirements, StructuralOutcome Outcome, string Why)
{
    public bool Counts => Outcome is StructuralOutcome.Preserved or StructuralOutcome.ArchitectureRule;
}
