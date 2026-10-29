namespace Lockflight.Cli.Config;

/// <summary>
/// The manifest's <c>ci:</c> section, read by the CI judge from the target branch only: how many red commits a pull request may
/// have, the judge job's time budget in minutes, how often a failing test is re-run at head, which paths make a change low-risk,
/// test inputs outside the test projects, and whether <c>redgreen red</c> commits the tree it judged.
/// </summary>
internal sealed record CiSettings(int MaxRedCommits, int TimeoutMinutes, int FlakyRetries, IReadOnlyList<string> LowRiskPaths, IReadOnlyList<string> TestInputs, bool RedCommits)
{
    public const int DefaultMaxRedCommits = 10;
    public const int MaxRedCommitsCeiling = 50;
    public const int DefaultTimeoutMinutes = 45;
    public const int TimeoutMinutesCeiling = 360;
    public const int DefaultFlakyRetries = 2;
    public const int FlakyRetriesCeiling = 5;

    /// <summary>Documentation and issue templates: a change only to these needs no red/green cycle.</summary>
    public static IReadOnlyList<string> DefaultLowRiskPaths { get; } = ["docs/**", "*.md", ".github/ISSUE_TEMPLATE/**"];

    public static CiSettings Default { get; } = new(DefaultMaxRedCommits, DefaultTimeoutMinutes, DefaultFlakyRetries, DefaultLowRiskPaths, [], RedCommits: true);

    public bool FromManifest { get; init; }
}
