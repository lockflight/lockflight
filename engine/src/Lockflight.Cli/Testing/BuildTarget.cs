namespace Lockflight.Cli.Testing;

/// <summary>A directory a test workspace builds, with an optional solution or project file and the test framework it prefers.</summary>
internal sealed record BuildTarget(string Directory, string? BuildFile, string? PreferredFramework);
