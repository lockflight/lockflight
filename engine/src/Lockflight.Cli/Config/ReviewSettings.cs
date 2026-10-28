namespace Lockflight.Cli.Config;

/// <summary>
/// The manifest's <c>review:</c> section: the model and the budget (US dollars, an estimate at API prices on a subscription) a review
/// must use to count as the review of record, and the branch the change targets (<see cref="Target"/>; null for the repository's
/// default branch), and how much of the diff the reviewer is shown (<see cref="MaxDiffCharacters"/>). <see cref="FromManifest"/>
/// says whether the manifest set them or the defaults apply.
/// </summary>
internal sealed record ReviewSettings(string Model, decimal BudgetUsd)
{
    public const string DefaultModel = "sonnet";
    public const decimal DefaultBudgetUsd = 3.00m;
    public const decimal MaxBudgetUsd = 100m;
    public const int DefaultMaxDiffCharacters = 120_000;
    public const int MinDiffCharacters = 10_000;
    public const int DiffCharactersCeiling = 400_000;

    public static ReviewSettings Default { get; } = new(DefaultModel, DefaultBudgetUsd);

    /// <summary>The target branch (<c>review.target</c>); null means <c>origin/HEAD</c>'s branch, else <c>main</c>.</summary>
    public string? Target { get; init; }

    /// <summary>
    /// The most characters of the diff the reviewer is shown (<c>review.maxDiffCharacters</c>), between
    /// <see cref="MinDiffCharacters"/> and <see cref="DiffCharactersCeiling"/>; a longer diff is cut and the run is not of record.
    /// </summary>
    public int MaxDiffCharacters { get; init; } = DefaultMaxDiffCharacters;

    public bool FromManifest { get; init; }
}
