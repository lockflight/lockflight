namespace Lockflight.Cli.Testing;

/// <summary>
/// A directory a test workspace builds, with an optional solution or project file, the test framework it prefers and the
/// <c>test.stopFilter</c> its whole-suite regression runs apply (null for none).
/// </summary>
internal sealed record BuildTarget(string Directory, string? BuildFile, string? PreferredFramework, string? StopFilter = null);
