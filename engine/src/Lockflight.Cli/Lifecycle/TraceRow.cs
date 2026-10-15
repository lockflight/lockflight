namespace Lockflight.Cli.Lifecycle;

/// <summary>
/// One requirement in the trace matrix: the red-then-green tests that still count for it (short <c>Class.Method</c> names), the
/// ones that no longer do with the reason (e.g. <c>Shipping.FirstKilo (deleted or renamed)</c>), and whether a test in an open
/// cycle names it.
/// </summary>
internal sealed record TraceRow(string RequirementId, IReadOnlyList<string> Tests, IReadOnlyList<string> Lost, bool Pending)
{
    public bool Covered => Tests.Count > 0;
}
