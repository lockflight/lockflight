namespace Lockflight.Cli.Lifecycle;

/// <summary>A functional requirement declared in spec.md as <c>- **FR-001**: …</c>.</summary>
internal sealed record Requirement(string Id, int Line, string Text);
