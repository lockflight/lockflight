namespace Lockflight.Cli.Testing;

/// <summary>The test applications found under a root, and the projects whose evaluation failed or timed out (none when discovery is complete).</summary>
internal sealed record TestAppDiscovery(IReadOnlyList<TestApp> Apps, IReadOnlyList<UnevaluatedProject> Unevaluated);
