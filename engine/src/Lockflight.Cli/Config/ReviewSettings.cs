namespace Lockflight.Cli.Config;

/// <summary>
/// The manifest's <c>review:</c> section: the model and the budget (US dollars, an estimate at API prices on a subscription) a review
/// must use to count as the review of record. <see cref="FromManifest"/> says whether the manifest set them or the defaults apply.
/// </summary>
internal sealed record ReviewSettings(string Model, decimal BudgetUsd)
{
    public const string DefaultModel = "opus";
    public const decimal DefaultBudgetUsd = 3.00m;
    public const decimal MaxBudgetUsd = 100m;

    public static ReviewSettings Default { get; } = new(DefaultModel, DefaultBudgetUsd);

    public bool FromManifest { get; init; }
}
