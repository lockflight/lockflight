namespace Lockflight.Cli.Testing;

/// <summary>Why <c>dotnet msbuild -getProperty</c> could not say whether a project is a test application.</summary>
internal enum ProjectEvaluationFailure
{
    /// <summary>The evaluation exited non-zero, or printed no properties to read.</summary>
    Failed = 0,

    /// <summary>The evaluation was killed when the discovery's time limit ran out.</summary>
    TimedOut = 1,
}
