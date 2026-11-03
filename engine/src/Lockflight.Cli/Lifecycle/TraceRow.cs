namespace Lockflight.Cli.Lifecycle;

/// <summary>
/// One requirement in the trace matrix: the red-then-green tests that still count for it (short <c>Class.Method</c> names), the
/// ones that no longer do with the reason (e.g. <c>Shipping.FirstKilo (deleted or renamed)</c>), whether a test in an open
/// cycle names it, and, for a requirement marked <c>[outside .NET: …]</c>, what its files say.
/// </summary>
internal sealed record TraceRow(string RequirementId, IReadOnlyList<string> Tests, IReadOnlyList<string> Lost, bool Pending)
{
    /// <summary>The files of the requirement's <c>[outside .NET: …]</c> marker; empty when it has none.</summary>
    public IReadOnlyList<OutsideFile> Outside { get; init; } = [];

    public bool Covered => Tests.Count > 0;

    /// <summary>
    /// Verified outside .NET and not covered by a .NET test: neither covered nor failing. A requirement whose .NET test no longer
    /// counts (<see cref="Lost"/>) is not excluded: the marker does not hide a test that was lost.
    /// </summary>
    public bool Excluded => !Covered && Lost.Count == 0 && Outside.Count > 0 && Outside.All(file => file.State == OutsideFileState.Names);

    /// <summary>
    /// What the outside .NET marker came to: <c>verified outside .NET (web/card.spec.ts)</c>, or why it does not count; null without
    /// a marker.
    /// </summary>
    public string? OutsideNote =>
        Outside.Count == 0 ? null
        : Outside.All(file => file.State == OutsideFileState.Names) && Lost.Count > 0 ? "marked outside .NET, but a .NET test for it no longer counts"
        : Outside.All(file => file.State == OutsideFileState.Names) ? $"verified outside .NET ({string.Join("; ", Outside.Select(Shown))})"
        : string.Join("; ", Outside.Where(file => file.State != OutsideFileState.Names).Select(Problem));

    private static string Shown(OutsideFile file) => file.Change is string change ? $"{file.Path}, {change}" : file.Path;

    private string Problem(OutsideFile file) => file.State switch
    {
        OutsideFileState.Missing => $"{file.Path} does not exist",
        OutsideFileState.DoesNotName => $"{file.Path} does not name {RequirementId}",
        OutsideFileState.InSpecs => $"{file.Path} is under specs/, which names every requirement and verifies none",
        _ => $"{file.Path} is outside the project",
    };
}
