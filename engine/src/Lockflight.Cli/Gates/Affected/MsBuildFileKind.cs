namespace Lockflight.Cli.Gates.Affected;

/// <summary>How an MSBuild file reaches a project, which decides what it may add to the project graph.</summary>
internal enum MsBuildFileKind
{
    /// <summary>The project file itself: the only place a project reference is read from.</summary>
    Project = 0,

    /// <summary>A file the project imports explicitly (a shared project's <c>.projitems</c>, a <c>.props</c>): its test markers count.</summary>
    Imported = 1,

    /// <summary>A <c>Directory.Build</c> or <c>Directory.Packages</c> file above the project, or one it imports: a test marker is a hint only.</summary>
    Inherited = 2,
}
