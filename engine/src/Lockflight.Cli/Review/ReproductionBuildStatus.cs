namespace Lockflight.Cli.Review;

/// <summary>How a build of reproductions ended.</summary>
internal enum ReproductionBuildStatus
{
    /// <summary>The reproductions compiled and the runner reported on them.</summary>
    Ran = 1,

    /// <summary>The build failed; the errors say why.</summary>
    DidNotCompile = 2,

    /// <summary>The build compiled but the test assembly could not be located or run.</summary>
    NotRun = 3,
}
