namespace Lockflight.Cli.Routing;

/// <summary>One project a project file references: the path as written, the resolved path (null when it cannot be resolved without evaluating the build), and the 1-based line.</summary>
internal sealed record ProjectReference(string Written, string? Target, int Line);
