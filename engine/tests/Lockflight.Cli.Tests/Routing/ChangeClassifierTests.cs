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
public sealed class ChangeClassifierTests
{
    public static TheoryData<string> Cases => [.. RoutingCase.Names()];

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EachFixtureIsClassifiedAsItsGoldenSays(string name)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        (ChangeSet changeSet, Manifest? manifest) = await ReadAsync(name, extraFile: null, cancellationToken);

        Classification classification = ChangeClassifier.Classify(changeSet, Policy(manifest), []);

        VerifyFixtureGolden(name, Render(classification));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task TheClassificationDoesNotDependOnFileOrder(string name)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        (ChangeSet changeSet, Manifest? manifest) = await ReadAsync(name, extraFile: null, cancellationToken);
        ChangeSet reversed = changeSet with { Files = [.. changeSet.Files.Reverse()] };
        ChangeSet shuffled = changeSet with { Files = [.. changeSet.Files.OrderBy(file => file.Path.Length).ThenByDescending(file => file.Path, StringComparer.Ordinal)] };

        string expected = Render(ChangeClassifier.Classify(changeSet, Policy(manifest), []));

        Assert.Equal(expected, Render(ChangeClassifier.Classify(reversed, Policy(manifest), [])));
        Assert.Equal(expected, Render(ChangeClassifier.Classify(shuffled, Policy(manifest), [])));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task RaisesAndStricterPresetsNeverLowerTheClass(string name)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        (ChangeSet changeSet, Manifest? manifest) = await ReadAsync(name, extraFile: null, cancellationToken);
        RoutingPolicy policy = Policy(manifest);
        Classification plain = ChangeClassifier.Classify(changeSet, policy, []);

        foreach (ChangeClass raise in Enum.GetValues<ChangeClass>())
        {
            Classification raised = ChangeClassifier.Classify(changeSet, policy, [new ClassRaise("label", raise)]);
            Assert.True(raised.Class >= plain.Class && raised.Class >= raise, $"a raise to {raise} gave {raised.Class}");
            Assert.Equal(plain.Computed, raised.Computed);
        }

        ChangeClass previous = ChangeClass.Fix;
        foreach (RoutingPreset preset in new[] { RoutingPreset.Relaxed, RoutingPreset.Default, RoutingPreset.Strict })
        {
            RoutingSettings stricter = RoutingPresets.For(preset) with
            {
                Critical = policy.Settings.Critical,
                Layers = policy.Settings.Layers,
                GeneratedPaths = policy.Settings.GeneratedPaths,
                NonProduction = policy.Settings.NonProduction,
            };
            ChangeClass atPreset = ChangeClassifier.Classify(changeSet, policy with { Settings = stricter }, []).Class;
            Assert.True(atPreset >= previous, $"{preset} gave {atPreset}, below the previous preset's {previous}");
            previous = atPreset;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task AddingAProductionFileNeverLowersTheClass(string name)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        (ChangeSet changeSet, Manifest? manifest) = await ReadAsync(name, extraFile: null, cancellationToken);
        (ChangeSet larger, _) = await ReadAsync(name, extraFile: "tools/Extra.cs", cancellationToken);

        Classification before = ChangeClassifier.Classify(changeSet, Policy(manifest), []);
        Classification after = ChangeClassifier.Classify(larger, Policy(manifest), []);

        Assert.True(after.Class >= before.Class, $"{after.Class} after adding a file, {before.Class} before");
        Assert.True(after.Class >= ChangeClass.Behaviour);
    }

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

    private static void VerifyFixtureGolden(string name, string actual)
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
