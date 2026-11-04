namespace Lockflight.Cli.Testing;

/// <summary>What a plain <c>dotnet test</c> run of one test said about it.</summary>
internal enum DotnetTestOutcome
{
    /// <summary>The run said nothing usable: it timed out, broke, wrote no report or ran no test.</summary>
    Inconclusive = 0,

    /// <summary>The test ran and passed.</summary>
    Passed = 1,

    /// <summary>The test ran and failed.</summary>
    Failed = 2,
}
