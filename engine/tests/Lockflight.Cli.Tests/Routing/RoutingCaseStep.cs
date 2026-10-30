namespace Lockflight.Cli.Tests.Routing;

/// <summary>One edit of a routing fixture's pull request: the operation, the path, the target of a move, and the content.</summary>
internal sealed record RoutingCaseStep(string Operation, string Path, string? Target, string Content);
