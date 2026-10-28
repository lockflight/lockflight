using Lockflight.Cli.Gates.Parsers;

namespace Lockflight.Cli.Gates.Baselines;

/// <summary>
/// Advisories a team has accepted for now (<c>.lockflight/baselines/vulnerable-packages.json</c>, protected), keyed by package,
/// version and advisory, so an upgrade or a new advisory is never silently covered. Written by <c>lockflight baseline deps</c>.
/// </summary>
internal sealed class AcceptedAdvisories(string projectRoot, string? unitPath)
{
    public const string FileName = "vulnerable-packages.json";

    private const string Schema = "lockflight.baseline.deps/v1";

    /// <summary>The file relative to the project root: under the unit's folder for a unit of a workspace.</summary>
    public string RelativePath => BaselinePaths.Relative(unitPath, FileName);

    private BaselineList List => new(BaselinePaths.Full(projectRoot, unitPath, FileName), Schema, "advisories");

    public IReadOnlySet<string> Load() => List.Load();

    public void Save(IEnumerable<VulnerablePackage> packages) => Save(packages.Select(package => package.Fingerprint));

    public void Save(IEnumerable<string> fingerprints) => List.Save(fingerprints);
}
