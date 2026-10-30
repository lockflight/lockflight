namespace Lockflight.Cli.Routing;

/// <summary>A changed line of a project or props file, whether it was added, and what it does.</summary>
internal sealed record ProjectLine(RoutedLine Line, bool Added, ProjectLineKind Kind);
