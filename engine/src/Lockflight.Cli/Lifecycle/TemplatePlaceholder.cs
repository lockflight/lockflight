namespace Lockflight.Cli.Lifecycle;

/// <summary>Spec Kit template text still in an artifact, such as <c>[FEATURE NAME]</c>, and the line it is on.</summary>
internal sealed record TemplatePlaceholder(int Line, string Text);
