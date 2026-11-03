using System.Text;
using Lockflight.Cli.Config;
using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Tests.Routing;

/// <summary>
/// Table-driven over <c>Routing/Fixtures/*.case</c>: each fixture's change is committed on a branch, read from git as CI reads it,
/// classified under the base branch's manifest, and compared with its golden. The properties hold for every fixture: the
/// classification does not depend on file order, a raise never lowers the class, a stricter preset never lowers it, and adding a
/// production file never lowers it.
/// </summary>
public abstract class ChangeClassifierCases
{
    // A test class runs its rows one after another, and a row that adds a file reads its case twice, so that theory's cases are
    // split in two classes at the first feature case; the two halves hold every case exactly once.
    private const string FirstFeatureCase = "feature-";

    public static TheoryData<string> Cases => [.. RoutingCase.Names()];

    public static TheoryData<string> CasesBeforeFeature => [.. RoutingCase.Names().Where(name => string.CompareOrdinal(name, FirstFeatureCase) < 0)];

    public static TheoryData<string> CasesFromFeature => [.. RoutingCase.Names().Where(name => string.CompareOrdinal(name, FirstFeatureCase) >= 0)];

    internal static string Render(Classification classification)
    {
        StringBuilder text = new();
        text.Append($"class: {ChangeClassNames.ToName(classification.Class)}\n");
        text.Append($"computed: {ChangeClassNames.ToName(classification.Computed)}\n");
        foreach (ChangeSignal signal in classification.Signals)
        {
            string strength = SignalStrengthNames.ToName(signal.Strength);
            text.Append($"signal: {signal.Id} -> {ChangeClassNames.ToName(signal.RaisesTo)} ({strength}) {(signal.Where.Length == 0 ? "-" : signal.Where)}: {signal.Detail}\n");
        }

        foreach (string failure in classification.Failures)
        {
            text.Append($"failure: {failure}\n");
        }

        return text.ToString();
    }

    internal static RoutingPolicy Policy(Manifest? manifest) =>
        RoutingPolicy.From(manifest, new PolicySource(RoutingRepository.BaseBranch, "base", ManifestBlob: null, "default"));

    internal static async Task<(ChangeSet ChangeSet, Manifest? Manifest)> ReadAsync(string name, string? extraFile, CancellationToken cancellationToken)
    {
        RoutingCase routingCase = RoutingCase.Load(name);
        using RoutingRepository repository = await RoutingRepository.CreateAsync(routingCase.BaseFiles, cancellationToken);
        routingCase.Apply(repository);
        if (extraFile is not null)
        {
            repository.Write(extraFile, "namespace Extra;\n\ninternal sealed class Extra\n{\n}\n");
        }

        await repository.CommitAsync("change", cancellationToken);
        ChangeSetReading reading = await ChangeSetReader.ReadAsync(repository.Root, RoutingRepository.BaseBranch, "HEAD", cancellationToken);
        Assert.True(reading.Status == ChangeSetReadStatus.Read, reading.Message);
        Manifest? manifest = null;
        if (routingCase.BaseFiles.TryGetValue(Manifest.FileName, out string? manifestText))
        {
            ManifestLoadResult parsed = new ManifestParser(repository.Root, Manifest.FileName).Parse(manifestText);
            Assert.True(parsed.Manifest is not null, parsed.Error?.Describe());
            manifest = parsed.Manifest;
        }

        return (reading.ChangeSet!, manifest);
    }

    internal static void VerifyFixtureGolden(string name, string actual)
    {
        string path = Path.Combine(RoutingCase.FixturesDirectory, name + ".expected.txt");
        if (Environment.GetEnvironmentVariable("LOCKFLIGHT_UPDATE_GOLDENS") == "1")
        {
            File.WriteAllText(path, actual);
            return;
        }

        Assert.True(File.Exists(path), $"{path} does not exist; run with LOCKFLIGHT_UPDATE_GOLDENS=1 to write it, then review it");
        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), actual);
    }
}
