namespace Lockflight.Cli.Canaries;

/// <summary>Which canaries <c>doctor --canaries</c> plants.</summary>
internal enum CanaryMode
{
    /// <summary>
    /// The ones that need no build: guard decisions, command refusals, the in-session command checks, the judgement of planted test
    /// results, and the TaskCompleted hook (in a scratch git repository). Seconds.
    /// </summary>
    Fast = 0,

    /// <summary>Also the ones that build and run a generated .NET project: the real test gate and a real red. Needs the .NET SDK and NuGet.</summary>
    Full = 1,
}
