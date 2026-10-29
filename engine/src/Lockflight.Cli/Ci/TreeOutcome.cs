namespace Lockflight.Cli.Ci;

/// <summary>How building and listing one tree of the pull request ended.</summary>
internal enum TreeOutcome
{
    /// <summary>Built, and every test application listed its tests.</summary>
    Ok = 1,

    /// <summary>The build failed, also after a clean retry.</summary>
    CompileFailed = 2,

    /// <summary>A project could not be evaluated, so the test applications found may be incomplete.</summary>
    DiscoveryIncomplete = 3,

    /// <summary>No xUnit test application was found.</summary>
    NoTestApps = 4,

    /// <summary>A tool broke: git could not check the tree out, or a runner could not list its tests.</summary>
    Broken = 5,
}
