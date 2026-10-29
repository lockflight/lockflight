namespace Lockflight.Cli.Ci;

/// <summary>The evidence spellings of <see cref="TreeOutcome"/>: <c>ok</c>, <c>compile-red</c>, <c>discovery-incomplete</c>, <c>no-test-apps</c>, <c>broken</c>.</summary>
internal static class TreeOutcomeNames
{
    public static string ToName(TreeOutcome outcome) => outcome switch
    {
        TreeOutcome.Ok => "ok",
        TreeOutcome.CompileFailed => "compile-red",
        TreeOutcome.DiscoveryIncomplete => "discovery-incomplete",
        TreeOutcome.NoTestApps => "no-test-apps",
        TreeOutcome.Broken => "broken",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown tree outcome."),
    };
}
