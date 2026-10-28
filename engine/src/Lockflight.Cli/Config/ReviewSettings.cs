namespace Lockflight.Cli.Config;

/// <summary>
/// The manifest's <c>review:</c> section: the model and the budget (US dollars, an estimate at API prices on a subscription) a review
/// must use to count as the review of record, and the branch the change targets (<see cref="Target"/>; null for the repository's
/// default branch). <see cref="FromManifest"/> says whether the manifest set them or the defaults apply.
/// </summary>
internal sealed record ReviewSettings(string Model, decimal BudgetUsd)
{
    public const string DefaultModel = "sonnet";
    public const decimal DefaultBudgetUsd = 3.00m;
    public const decimal MaxBudgetUsd = 100m;

    public static ReviewSettings Default { get; } = new(DefaultModel, DefaultBudgetUsd);

    /// <summary>The target branch (<c>review.target</c>); null means <c>origin/HEAD</c>'s branch, else <c>main</c>.</summary>
    public string? Target { get; init; }

    public bool FromManifest { get; init; }
}
