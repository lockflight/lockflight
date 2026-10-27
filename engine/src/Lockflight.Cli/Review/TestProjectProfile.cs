namespace Lockflight.Cli.Review;

/// <summary>
/// What a reviewer needs to know about a test project to write a reproduction that compiles in it: its path (relative to the
/// repository, '/'-separated), target frameworks, test framework, the projects and packages it references, and the using
/// directives the engine puts in front of the reproductions.
/// </summary>
internal sealed record TestProjectProfile(
    string Name,
    string Path,
    string TargetFrameworks,
    string TestFramework,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> PackageReferences,
    IReadOnlyList<string> Usings);
