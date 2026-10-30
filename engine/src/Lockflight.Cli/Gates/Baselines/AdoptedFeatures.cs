namespace Lockflight.Cli.Gates.Baselines;

/// <summary>
/// Features that predate Lockflight (<c>.lockflight/baselines/adopted-features.json</c>, protected; written by the
/// <c>lockflight init</c> that creates the manifest). Until such a feature gets Lockflight records of its own, <c>approve verify --base</c> does not
/// require approvals when a change touches it, and <c>trace</c> expects no red/green evidence. CI reads baselines from the base
/// branch, so a feature added to the list in the same pull request is not exempt.
/// </summary>
internal sealed class AdoptedFeatures(string projectRoot)
{
    public const string FileName = "adopted-features.json";

    private const string Schema = "lockflight.baseline.features/v1";

    private BaselineList List => new(BaselinePaths.Full(projectRoot, unitPath: null, FileName), Schema, "features");

    public IReadOnlySet<string> Load() => List.Load();

    public void Save(IEnumerable<string> features) => List.Save(features);
}
