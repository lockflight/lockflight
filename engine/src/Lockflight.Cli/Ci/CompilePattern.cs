namespace Lockflight.Cli.Ci;

/// <summary>
/// A path glob at head that MSBuild compiles: the folder of the file that declares it, the glob as written relative to it, and
/// whether it is also read relative to each project's folder (an include in a build-wide file applies to every project under it).
/// </summary>
internal sealed record CompilePattern(string Directory, string Glob, bool OfEachProject);
