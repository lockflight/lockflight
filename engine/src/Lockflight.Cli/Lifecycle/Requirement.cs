namespace Lockflight.Cli.Lifecycle;

/// <summary>A functional requirement declared in spec.md as <c>- **FR-001**: …</c>, its text without the marker.</summary>
internal sealed record Requirement(string Id, int Line, string Text)
{
    /// <summary>The marker after the bold ID as written, without its brackets (<c>outside .NET: web/card.spec.ts</c>); null when none.</summary>
    public string? Marker { get; init; }

    /// <summary>The files, relative to the project root, that verify the requirement outside .NET; empty when it is not so marked.</summary>
    public IReadOnlyList<string> VerifiedOutside { get; init; } = [];
}
