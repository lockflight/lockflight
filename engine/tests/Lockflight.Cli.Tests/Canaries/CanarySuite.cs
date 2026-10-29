using Lockflight.Cli.Canaries;

namespace Lockflight.Cli.Tests.Canaries;

/// <summary>The engine's canary suite: how its tests judge a group of canaries, and the trait CI selects them by.</summary>
internal static class CanarySuite
{
    /// <summary>The trait every canary test carries: CI runs these in the <c>canaries</c> jobs and leaves them out of <c>engine</c>.</summary>
    public const string Trait = "Category";

    public const string TraitValue = "Canary";

    /// <summary>Fails with every canary that was not caught, named with what its control answered.</summary>
    public static void AssertAllCaught(IReadOnlyList<CanaryReport> reports)
    {
        Assert.NotEmpty(reports);
        string[] notCaught = [.. reports.Where(report => report.Result != CanaryResult.Caught).Select(report => $"{report.Result} {report.Name}: {report.Detail}")];
        Assert.True(notCaught.Length == 0, $"canaries not caught:\n{string.Join('\n', notCaught)}");
    }
}
