namespace Lockflight.Cli.Gates.Baselines;

/// <summary>
/// Suppressions a person has accepted (<c>.lockflight/baselines/suppressions.json</c>, protected), keyed by file and rule
/// (<c>src/Orders/Order.cs|CA1822</c>): that rule may be suppressed again in that file, any other file or rule still fails. Written by
/// <c>lockflight baseline suppressions</c>, which adds to what is already accepted rather than replacing it.
/// </summary>
internal sealed class AcceptedSuppressions(string projectRoot)
{
    public const string FileName = "suppressions.json";

    private const string Schema = "lockflight.baseline.suppressions/v1";

    /// <summary>The file relative to the project root; one per project, since the step judges the whole project's changes.</summary>
    public static string RelativePath { get; } = BaselinePaths.Relative(unitPath: null, FileName);

    private BaselineList List => new(BaselinePaths.Full(projectRoot, unitPath: null, FileName), Schema, "accepted");

    public IReadOnlySet<string> Load() => List.Load();

    public void Save(IEnumerable<string> keys) => List.Save(keys);
}
